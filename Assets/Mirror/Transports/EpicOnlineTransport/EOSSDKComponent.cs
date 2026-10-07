using Epic.OnlineServices;
using Epic.OnlineServices.Logging;
using Epic.OnlineServices.Platform;

using System;
using System.Runtime.InteropServices;

using UnityEngine;

/// <summary>
/// Manages the Epic Online Services SDK
/// Do not destroy this component!
/// The Epic Online Services SDK can only be initialized once,
/// after releasing the SDK the game has to be restarted in order to initialize the SDK again.
/// In the unity editor the OnDestroy function will not run so that we dont have to restart the editor after play.
/// </summary>
namespace EpicTransport {
    [DefaultExecutionOrder(-32000)]
    public class EOSSDKComponent : MonoBehaviour {

        // Unity Inspector shown variables
        
        [SerializeField]
        private EosApiKey apiKeys;

        [Header("User Login")]
        public bool authInterfaceLogin = false;
        public Epic.OnlineServices.Auth.LoginCredentialType authInterfaceCredentialType = Epic.OnlineServices.Auth.LoginCredentialType.AccountPortal;
        public uint devAuthToolPort = 7878;
        public string devAuthToolCredentialName = "";
        public Epic.OnlineServices.ExternalCredentialType connectInterfaceCredentialType = Epic.OnlineServices.ExternalCredentialType.DeviceidAccessToken;
        public string deviceModel = "PC Windows 64bit";
        [SerializeField] private string displayName = "User";
        public static string DisplayName {
            get {
                return Instance.displayName;
            }
            set {
                Instance.displayName = value;
            }
        }

        [Header("Misc")]
        public LogLevel epicLoggerLevel = LogLevel.Info;

        [SerializeField] private bool collectPlayerMetrics = true;
        public static bool CollectPlayerMetrics {
            get {
                return Instance.collectPlayerMetrics;
            }
        }

        public bool checkForEpicLauncherAndRestart = false;
        public bool delayedInitialization = false;
        public float platformTickIntervalInSeconds = 0.0f;
        private float platformTickTimer = 0f;
        public uint tickBudgetInMilliseconds = 0;

        // End Unity Inspector shown variables

        private ulong authExpirationHandle;


        private string authInterfaceLoginCredentialId = null;
        public static void SetAuthInterfaceLoginCredentialId(string credentialId) => Instance.authInterfaceLoginCredentialId = credentialId;
        private string authInterfaceCredentialToken = null;
        public static void SetAuthInterfaceCredentialToken(string credentialToken) => Instance.authInterfaceCredentialToken = credentialToken;
        private string connectInterfaceCredentialToken = null;
        public static void SetConnectInterfaceCredentialToken(string credentialToken) => Instance.connectInterfaceCredentialToken = credentialToken;

        private PlatformInterface EOS;

        // Interfaces with safe null checking
        public static Epic.OnlineServices.Achievements.AchievementsInterface GetAchievementsInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetAchievementsInterface() : null;
        public static Epic.OnlineServices.Auth.AuthInterface GetAuthInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetAuthInterface() : null;
        public static Epic.OnlineServices.Connect.ConnectInterface GetConnectInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetConnectInterface() : null;
        public static Epic.OnlineServices.Ecom.EcomInterface GetEcomInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetEcomInterface() : null;
        public static Epic.OnlineServices.Friends.FriendsInterface GetFriendsInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetFriendsInterface() : null;
        public static Epic.OnlineServices.Leaderboards.LeaderboardsInterface GetLeaderboardsInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetLeaderboardsInterface() : null;
        public static Epic.OnlineServices.Lobby.LobbyInterface GetLobbyInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetLobbyInterface() : null;
        public static Epic.OnlineServices.Metrics.MetricsInterface GetMetricsInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetMetricsInterface() : null;
        public static Epic.OnlineServices.Mods.ModsInterface GetModsInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetModsInterface() : null;
        public static Epic.OnlineServices.P2P.P2PInterface GetP2PInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetP2PInterface() : null;
        public static Epic.OnlineServices.PlayerDataStorage.PlayerDataStorageInterface GetPlayerDataStorageInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetPlayerDataStorageInterface() : null;
        public static Epic.OnlineServices.Presence.PresenceInterface GetPresenceInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetPresenceInterface() : null;
        public static Epic.OnlineServices.Sessions.SessionsInterface GetSessionsInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetSessionsInterface() : null;
        public static Epic.OnlineServices.Stats.StatsInterface GetStatsInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetStatsInterface() : null;
        public static Epic.OnlineServices.TitleStorage.TitleStorageInterface GetTitleStorageInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetTitleStorageInterface() : null;
        public static Epic.OnlineServices.UI.UIInterface GetUIInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetUIInterface() : null;
        public static Epic.OnlineServices.UserInfo.UserInfoInterface GetUserInfoInterface() => (instance != null && instance.EOS != null) ? instance.EOS.GetUserInfoInterface() : null;


        protected EpicAccountId localUserAccountId;
        public static EpicAccountId LocalUserAccountId => instance != null ? instance.localUserAccountId : null;

        protected string localUserAccountIdString;
        public static string LocalUserAccountIdString => instance != null ? instance.localUserAccountIdString : string.Empty;

        protected ProductUserId localUserProductId;
        public static ProductUserId LocalUserProductId => instance != null ? instance.localUserProductId : null;

        protected string localUserProductIdString;
        public static string LocalUserProductIdString => instance != null ? instance.localUserProductIdString : string.Empty;

        protected bool initialized;
        public static bool Initialized => instance != null && instance.initialized;

        protected bool isConnecting;
        public static bool IsConnecting => instance != null && instance.isConnecting;

        protected static EOSSDKComponent instance;
        protected static EOSSDKComponent Instance {
            get {
                if (instance == null) {
                    return new GameObject("EOSSDKComponent").AddComponent<EOSSDKComponent>();
                } else {
                    return instance;
                }
            }
        }

        public static void Tick() {
            if (instance != null && instance.EOS != null) {
                instance.platformTickTimer -= Time.deltaTime;
                instance.EOS.Tick();
            }
        }

        // If we're in editor, we should dynamically load and unload the SDK between play sessions.
        // This allows us to initialize the SDK each time the game is run in editor.
#if UNITY_EDITOR_WIN
        [DllImport("Kernel32.dll")]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("Kernel32.dll")]
        private static extern int FreeLibrary(IntPtr hLibModule);

        [DllImport("Kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint GetModuleFileName(IntPtr hModule, [Out] System.Text.StringBuilder lpFilename, int nSize);

        private IntPtr libraryPointer;
#endif
        
#if UNITY_EDITOR_LINUX
        [DllImport("libdl.so", EntryPoint = "dlopen")]
        private static extern IntPtr LoadLibrary(String lpFileName, int flags = 2);   

        [DllImport("libdl.so", EntryPoint = "dlclose")]
        private static extern int FreeLibrary(IntPtr hLibModule);
    
        [DllImport("libdl.so")]
        private static extern IntPtr dlsym(IntPtr handle, String symbol);

        [DllImport("libdl.so")]
        private static extern IntPtr dlerror();

        private static IntPtr GetProcAddress(IntPtr hModule, string lpProcName) {
            // clear previous errors if any
            dlerror();
            var res = dlsym(hModule, lpProcName);
            var errPtr = dlerror();
            if (errPtr != IntPtr.Zero) {
                throw new Exception("dlsym: " + Marshal.PtrToStringAnsi(errPtr));
            }
            return res;
        }    
        private IntPtr libraryPointer;
#endif

        private void Awake() {
            // Initialize Java version of the SDK with a reference to the VM with JNI
            // See https://eoshelp.epicgames.com/s/question/0D54z00006ufJBNCA2/cant-get-createdeviceid-to-work-in-unity-android-c-sdk?language=en_US
            if (Application.platform == RuntimePlatform.Android)
            {
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                AndroidJavaObject context = activity.Call<AndroidJavaObject>("getApplicationContext");
                AndroidJavaClass EOS_SDK_JAVA = new AndroidJavaClass("com.epicgames.mobile.eossdk.EOSSDK");
                EOS_SDK_JAVA.CallStatic("init", context);
            }
            
            // Prevent multiple instances
            if (instance != null) {
                Destroy(gameObject);
                return;
            }
            instance = this;

#if UNITY_EDITOR
            string dllFileName = Config.LibraryName.EndsWith(".dll") ? Config.LibraryName : Config.LibraryName + ".dll";
            string libraryPath = System.IO.Path.Combine(Application.dataPath, "Mirror/Transports/EpicOnlineTransport/EOSSDK", dllFileName);

            if (!System.IO.File.Exists(libraryPath)) {
                libraryPath = System.IO.Path.Combine(Application.dataPath, "Mirror/Runtime/Transport/EpicOnlineTransport/EOSSDK", dllFileName);
            }

            libraryPointer = LoadLibrary(libraryPath);
            if (libraryPointer == IntPtr.Zero) {
                throw new Exception("Failed to load library: " + libraryPath);
            }

#if UNITY_EDITOR_WIN
            System.Text.StringBuilder sb = new System.Text.StringBuilder(1024);
            GetModuleFileName(libraryPointer, sb, 1024);
            string actualLoadedPath = sb.ToString();
            long actualSize = System.IO.File.Exists(actualLoadedPath) ? new System.IO.FileInfo(actualLoadedPath).Length : -1;
            Debug.Log($"<color=#FFFF00>[EOSSDKComponent] Loaded EOS native library: '{actualLoadedPath}' (Size: {actualSize:N0} bytes, Handle: 0x{libraryPointer.ToInt64():X})</color>");
            if (actualSize == 23206368) {
                Debug.LogError("<color=#FF0000>[EOSSDKComponent] CRITICAL: Unity process is still running the old EOS SDK 1.13 DLL cached in memory from before the upgrade! You MUST completely close Unity Editor and restart it for the v1.19 SDK to take effect.</color>");
            } else if (actualSize == 19548600) {
                Debug.Log("<color=#55FF55>[EOSSDKComponent] Verified: EOS SDK v1.19.2.1 native DLL is successfully loaded into memory!</color>");
            }
#endif

            Bindings.Hook(libraryPointer, GetProcAddress);
#endif

            if (!delayedInitialization) {
                Initialize();
            }
        }

        protected void InitializeImplementation() {
            isConnecting = true;

            var initializeOptions = new InitializeOptions() {
                ProductName = apiKeys.epicProductName,
                ProductVersion = apiKeys.epicProductVersion
            };

            var initializeResult = PlatformInterface.Initialize(initializeOptions);

            // This code is called each time the game is run in the editor, so we catch the case where the SDK has already been initialized in the editor.
            var isAlreadyConfiguredInEditor = Application.isEditor && initializeResult == Result.AlreadyConfigured;
            if (initializeResult != Result.Success && !isAlreadyConfiguredInEditor) {
                throw new System.Exception("Failed to initialize platform: " + initializeResult);
            }

            // The SDK outputs diagnostic information. In editor, use epicLoggerLevel (default Warning/Info). In player, only log Errors.
#if UNITY_EDITOR
            LoggingInterface.SetLogLevel(LogCategory.AllCategories, epicLoggerLevel);
            LoggingInterface.SetCallback(message => Logger.EpicDebugLog(message));
            Debug.Log($"[EOSSDKComponent] Initializing Platform with ProductId: {apiKeys.epicProductId}, SandboxId: {apiKeys.epicSandboxId}, DeploymentId: {apiKeys.epicDeploymentId}, ClientId: {apiKeys.epicClientId}");
#else
            LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.Error);
#endif

            var options = new Options() {
                ProductId = apiKeys.epicProductId,
                SandboxId = apiKeys.epicSandboxId,
                DeploymentId = apiKeys.epicDeploymentId,
                ClientCredentials = new ClientCredentials() {
                    ClientId = apiKeys.epicClientId,
                    ClientSecret = apiKeys.epicClientSecret
                },
                TickBudgetInMilliseconds = tickBudgetInMilliseconds
            };

            if (EOS == null) {
                EOS = PlatformInterface.Create(options);
                if (EOS == null) {
                    throw new System.Exception("Failed to create platform");
                }
            }

            if (checkForEpicLauncherAndRestart) {
                Result result = EOS.CheckForLauncherAndRestart();

                // If not started through epic launcher the app will be restarted and we can quit 
                if (result != Result.NoChange) {

                    // Log error if launcher check failed, but still quit to prevent hacking
                    if (result == Result.UnexpectedError) {
                        Debug.LogError("Unexpected Error while checking if app was started through epic launcher");
                    }

                    Application.Quit();
                }
            }

            // If we use the Auth interface then only login into the Connect interface after finishing the auth interface login
            // If we use the Auth interface then only login into the Connect interface after finishing the auth interface login
            // If we don't use the Auth interface we can directly login to the Connect interface
            if (authInterfaceLogin) {
                if (authInterfaceCredentialType == Epic.OnlineServices.Auth.LoginCredentialType.Developer) {
                    authInterfaceLoginCredentialId = "127.0.0.1:" + devAuthToolPort;
                    authInterfaceCredentialToken = devAuthToolCredentialName;
                } else {
                    authInterfaceLoginCredentialId = null;
                    authInterfaceCredentialToken = null;
                }

                // Login to Auth Interface
                Epic.OnlineServices.Auth.LoginOptions loginOptions = new Epic.OnlineServices.Auth.LoginOptions() {
                    Credentials = new Epic.OnlineServices.Auth.Credentials() {
                        Type = authInterfaceCredentialType,
                        Id = authInterfaceLoginCredentialId,
                        Token = authInterfaceCredentialToken
                    },
                    ScopeFlags = Epic.OnlineServices.Auth.AuthScopeFlags.BasicProfile
                };

                EOS.GetAuthInterface().Login(loginOptions, null, OnAuthInterfaceLogin);
            } else {
                // Login to Connect Interface
                if (connectInterfaceCredentialType == Epic.OnlineServices.ExternalCredentialType.DeviceidAccessToken) {
                    Epic.OnlineServices.Connect.CreateDeviceIdOptions createDeviceIdOptions = new Epic.OnlineServices.Connect.CreateDeviceIdOptions();
                    createDeviceIdOptions.DeviceModel = deviceModel;
                    EOS.GetConnectInterface().CreateDeviceId(createDeviceIdOptions, null, OnCreateDeviceId);
                } else {
                    ConnectInterfaceLogin();
                }
            }

        }
        public static void Initialize() {
            if (Instance.initialized || Instance.isConnecting) {
                return;
            }

            Instance.InitializeImplementation();
        }

        public static event System.Action<Result> OnLoginFailed;
        public static event System.Action OnLoginSuccess;

        public static void LoginWithEpicAccount() {
            if (Instance.initialized) return;
            Instance.isConnecting = false;
            Instance.authInterfaceLogin = true;
            Instance.authInterfaceCredentialType = Epic.OnlineServices.Auth.LoginCredentialType.AccountPortal;
            Instance.connectInterfaceCredentialType = Epic.OnlineServices.ExternalCredentialType.Epic;
            Instance.InitializeImplementation();
        }

        public static void LoginWithDevAuth(string credentialName, uint port = 7878) {
            if (Instance.initialized) return;
            Instance.isConnecting = false;
            Instance.authInterfaceLogin = true;
            Instance.authInterfaceCredentialType = Epic.OnlineServices.Auth.LoginCredentialType.Developer;
            Instance.devAuthToolPort = port;
            Instance.devAuthToolCredentialName = credentialName;
            Instance.connectInterfaceCredentialType = Epic.OnlineServices.ExternalCredentialType.Epic;
            Instance.InitializeImplementation();
        }

        public static void LoginWithDeviceId(string displayName = "User") {
            if (Instance.initialized) return;
            Instance.isConnecting = false;
            Instance.authInterfaceLogin = false;
            Instance.connectInterfaceCredentialType = Epic.OnlineServices.ExternalCredentialType.DeviceidAccessToken;
            Instance.displayName = displayName;
            Instance.InitializeImplementation();
        }

        public static void LoginWithSteam(string steamTicketHex, string displayName = "SteamUser") {
            if (Instance.initialized) return;
            Instance.isConnecting = false;
            Instance.authInterfaceLogin = false;
            Instance.connectInterfaceCredentialType = Epic.OnlineServices.ExternalCredentialType.SteamSessionTicket;
            Instance.connectInterfaceCredentialToken = steamTicketHex;
            Instance.displayName = displayName;
            Instance.InitializeImplementation();
        }

        public static void DeleteGuestDeviceId(System.Action<Result> onComplete = null) {
            if (instance != null && instance.EOS != null) {
                var connect = instance.EOS.GetConnectInterface();
                if (connect != null) {
                    connect.DeleteDeviceId(new Epic.OnlineServices.Connect.DeleteDeviceIdOptions(), null, (Epic.OnlineServices.Connect.DeleteDeviceIdCallbackInfo cb) => {
#if UNITY_EDITOR
                        Debug.Log("[EOSSDKComponent] DeleteDeviceId returned: " + cb.ResultCode);
#endif
                        onComplete?.Invoke(cb.ResultCode);
                    });
                } else {
                    onComplete?.Invoke(Result.NotConfigured);
                }
            } else {
                onComplete?.Invoke(Result.NotConfigured);
            }
        }

        private void OnAuthInterfaceLogin(Epic.OnlineServices.Auth.LoginCallbackInfo loginCallbackInfo) {
            if (loginCallbackInfo.ResultCode == Result.Success) {
#if UNITY_EDITOR
                Debug.Log("[EOSSDKComponent] Auth Interface Login succeeded");
#endif

                string accountIdString;
                Result result = loginCallbackInfo.LocalUserId.ToString(out accountIdString);
                if (Result.Success == result) {
#if UNITY_EDITOR
                    Debug.Log("[EOSSDKComponent] EOS User ID: " + accountIdString);
#endif

                    localUserAccountIdString = accountIdString;
                    localUserAccountId = loginCallbackInfo.LocalUserId;
                }
                
                ConnectInterfaceLogin();
            } else {
                Debug.LogError("[EOSSDKComponent] Auth Interface Login failed: " + loginCallbackInfo.ResultCode);
                isConnecting = false;
                OnLoginFailed?.Invoke(loginCallbackInfo.ResultCode);
            }
        }

        private void OnCreateDeviceId(Epic.OnlineServices.Connect.CreateDeviceIdCallbackInfo createDeviceIdCallbackInfo) {
            if (createDeviceIdCallbackInfo.ResultCode == Result.Success || createDeviceIdCallbackInfo.ResultCode == Result.DuplicateNotAllowed) {
                ConnectInterfaceLogin();
            } else {
                Debug.LogError("[EOSSDKComponent] Device ID creation failed: " + createDeviceIdCallbackInfo.ResultCode);
                isConnecting = false;
                OnLoginFailed?.Invoke(createDeviceIdCallbackInfo.ResultCode);
            }
        }

        private void ConnectInterfaceLogin() {
            var loginOptions = new Epic.OnlineServices.Connect.LoginOptions();

            if (connectInterfaceCredentialType == Epic.OnlineServices.ExternalCredentialType.Epic) {
                Epic.OnlineServices.Auth.Token token;
                Result result = EOS.GetAuthInterface().CopyUserAuthToken(new Epic.OnlineServices.Auth.CopyUserAuthTokenOptions(), localUserAccountId, out token);

                if (result == Result.Success) {
                    connectInterfaceCredentialToken = token.AccessToken;
                } else {
                    Debug.LogError("[EOSSDKComponent] Failed to retrieve User Auth Token: " + result);
                    isConnecting = false;
                    OnLoginFailed?.Invoke(result);
                    return;
                }
            } else if (connectInterfaceCredentialType == Epic.OnlineServices.ExternalCredentialType.DeviceidAccessToken) {
                loginOptions.UserLoginInfo = new Epic.OnlineServices.Connect.UserLoginInfo();
                loginOptions.UserLoginInfo.DisplayName = displayName;
            }

            loginOptions.Credentials = new Epic.OnlineServices.Connect.Credentials();
            loginOptions.Credentials.Type = connectInterfaceCredentialType;
            loginOptions.Credentials.Token = connectInterfaceCredentialToken;

#if UNITY_EDITOR
            Debug.Log($"<color=#00FFFF>[EOSSDKComponent] Initiating ConnectInterface.Login | Type: {connectInterfaceCredentialType} ({(int)connectInterfaceCredentialType}) | TokenLength: {connectInterfaceCredentialToken?.Length ?? 0}</color>");
#endif

            EOS.GetConnectInterface().Login(loginOptions, null, OnConnectInterfaceLogin);
        }

        private void OnConnectInterfaceLogin(Epic.OnlineServices.Connect.LoginCallbackInfo loginCallbackInfo) {
            if (loginCallbackInfo.ResultCode == Result.Success) {
#if UNITY_EDITOR
                Debug.Log("<color=#55FF55>[EOSSDKComponent] Connect Interface Login succeeded!</color>");
#endif

                string productIdString;
                Result result = loginCallbackInfo.LocalUserId.ToString(out productIdString);
                if (Result.Success == result) {
#if UNITY_EDITOR
                    Debug.Log("<color=#55FF55>[EOSSDKComponent] EOS User Product ID: " + productIdString + "</color>");
#endif

                    localUserProductIdString = productIdString;
                    localUserProductId = loginCallbackInfo.LocalUserId;
                }
                
                initialized = true;
                isConnecting = false;
                OnLoginSuccess?.Invoke();

                var authExpirationOptions = new Epic.OnlineServices.Connect.AddNotifyAuthExpirationOptions();
                authExpirationHandle = EOS.GetConnectInterface().AddNotifyAuthExpiration(authExpirationOptions, null, OnAuthExpiration);
            } else if (loginCallbackInfo.ResultCode == Result.InvalidUser) {
#if UNITY_EDITOR
                Debug.Log("[EOSSDKComponent] First-time login: Creating new EOS Connect user...");
#endif
                EOS.GetConnectInterface().CreateUser(new Epic.OnlineServices.Connect.CreateUserOptions() { ContinuanceToken = loginCallbackInfo.ContinuanceToken }, null, (Epic.OnlineServices.Connect.CreateUserCallbackInfo cb) => {
                    if (cb.ResultCode != Result.Success) {
                        Debug.LogError("[EOSSDKComponent] Connect CreateUser failed: " + cb.ResultCode);
                        isConnecting = false;
                        OnLoginFailed?.Invoke(cb.ResultCode);
                        return;
                    }
                    localUserProductId = cb.LocalUserId;
                    ConnectInterfaceLogin();
                });
            } else {
                Debug.LogError($"<color=#FF5555>[EOSSDKComponent] Connect Interface Login failed: {loginCallbackInfo.ResultCode} (Code: {(int)loginCallbackInfo.ResultCode}). Check the [EOS SDK] logs above for server diagnostic message.</color>");
                isConnecting = false;
                OnLoginFailed?.Invoke(loginCallbackInfo.ResultCode);
            }
        }
        
        private void OnAuthExpiration(Epic.OnlineServices.Connect.AuthExpirationCallbackInfo authExpirationCallbackInfo) {
            EOS.GetConnectInterface().RemoveNotifyAuthExpiration(authExpirationHandle);
            ConnectInterfaceLogin();
        }

        // Calling tick on a regular interval is required for callbacks to work.
        private void LateUpdate() {
            if (EOS != null) {
                platformTickTimer += Time.deltaTime;

                if (platformTickTimer >= platformTickIntervalInSeconds) {
                    platformTickTimer = 0;
                    EOS.Tick();
                }
            }
        }

        private void OnApplicationQuit() {
            if (EOS != null) {
                EOS.Release();
                EOS = null;
                PlatformInterface.Shutdown();
            }

            // Unhook the library in the editor, this makes it possible to load the library again after stopping to play
#if UNITY_EDITOR
            if (libraryPointer != IntPtr.Zero) {
                Bindings.Unhook();

                // Free until the module ref count is 0
                while (FreeLibrary(libraryPointer) != 0) { }

                libraryPointer = IntPtr.Zero;
            }
#endif
        }
    }
}