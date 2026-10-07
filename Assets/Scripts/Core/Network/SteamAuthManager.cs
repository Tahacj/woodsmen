using System;
using System.Collections;
using System.Text;
using EpicTransport;
using JustAGame;
using Steamworks;
using UnityEngine;

namespace JustAGame.Core.Network
{
    /// <summary>
    /// Manages Steamworks SDK lifecycle, WebApi session ticket retrieval,
    /// and seamlessly bridges Steam authentication into Epic Online Services (EOS) Connect Interface.
    /// Strictly adheres to repository DESIGN_PATTERNS.md zero-slop guidelines.
    /// </summary>
    [DisallowMultipleComponent]
    public class SteamAuthManager : MonoBehaviour
    {
        public const string EOS_IDENTITY = "epiconlineservices";

        public static SteamAuthManager Instance { get; private set; }

        public static bool IsSteamInitialized => _isInitialized;

        public string SteamPersonaName
        {
            get
            {
                try
                {
                    if (_isInitialized)
                    {
                        return SteamFriends.GetPersonaName();
                    }
                    else
                    {
                        return string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SteamAuthManager] Exception getting SteamPersonaName: {ex.Message}");
                    return string.Empty;
                }
            }
        }

        public string SteamIdString
        {
            get
            {
                try
                {
                    if (_isInitialized)
                    {
                        return SteamUser.GetSteamID().m_SteamID.ToString();
                    }
                    else
                    {
                        return string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SteamAuthManager] Exception getting SteamIdString: {ex.Message}");
                    return string.Empty;
                }
            }
        }

        public static event Action<string, string> OnSteamTicketAcquired;
        public static event Action<string> OnSteamAuthFailed;

        private static bool _isInitialized = false;
        private Callback<GetTicketForWebApiResponse_t> _getTicketForWebApiResponseCallback;
        private HAuthTicket _activeAuthTicket = HAuthTicket.Invalid;
        private Action<bool, string> _pendingLoginCallback = null;
        private Coroutine _ticketTimeoutCoroutine = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            try
            {
                if (Instance.IsNull())
                {
                    GameObject managerObj = new GameObject("SteamAuthManager", typeof(SteamAuthManager));
                    DontDestroyOnLoad(managerObj);
                }
                else
                {
                    // Instance already exists
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamAuthManager] Exception in AutoInitialize: {ex.Message}");
            }
        }

        private void Awake()
        {
            try
            {
                if (Instance.IsNull())
                {
                    Instance = this;
                    transform.SetParent(null);
                    DontDestroyOnLoad(gameObject);
                    InitializeSteamAPI();
                }
                else if (!ReferenceEquals(Instance, this))
                {
                    Destroy(gameObject);
                }
                else
                {
                    // Existing instance
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamAuthManager] Exception in Awake: {ex.Message}");
            }
        }

        private void InitializeSteamAPI()
        {
            try
            {
                if (!SteamAPI.IsSteamRunning())
                {
                    Debug.LogWarning("[SteamAuthManager] Steam desktop client is NOT running. Steam login will not be available.");
                    _isInitialized = false;
                    return;
                }
                else
                {
                    // Steam client active
                }

                _isInitialized = SteamAPI.Init();
                if (_isInitialized)
                {
                    string persona = SteamFriends.GetPersonaName();
                    CSteamID steamId = SteamUser.GetSteamID();
#if UNITY_EDITOR
                    Debug.Log($"<color=#55FF55>[SteamAuthManager] Steamworks initialized successfully! Logged in as: {persona} (SteamID: {steamId})</color>");
#endif

                    _getTicketForWebApiResponseCallback = Callback<GetTicketForWebApiResponse_t>.Create(HandleGetTicketForWebApiResponse);
                }
                else
                {
                    Debug.LogError("[SteamAuthManager] SteamAPI.Init() failed! Ensure steam_appid.txt exists in root with valid App ID.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamAuthManager] Exception in InitializeSteamAPI: {ex.Message}");
                _isInitialized = false;
            }
        }

        private void Update()
        {
            try
            {
                if (_isInitialized)
                {
                    SteamAPI.RunCallbacks();
                }
                else
                {
                    // Steam not initialized
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamAuthManager] Exception in Update: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (_isInitialized)
                {
                    if (_activeAuthTicket != HAuthTicket.Invalid)
                    {
                        SteamUser.CancelAuthTicket(_activeAuthTicket);
                        _activeAuthTicket = HAuthTicket.Invalid;
                    }
                    else
                    {
                        // No active ticket to cancel
                    }

                    SteamAPI.Shutdown();
                    _isInitialized = false;
                }
                else
                {
                    // Not initialized
                }

                if (ReferenceEquals(Instance, this))
                {
                    Instance = null;
                }
                else
                {
                    // Non-singleton instance
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamAuthManager] Exception in OnDestroy: {ex.Message}");
            }
        }

        /// <summary>
        /// Initiates Steam-to-EOS authentication.
        /// Requests a modern WebApi session ticket from Steam and forwards it to EOSSDKComponent.LoginWithSteam.
        /// </summary>
        public static void LoginToEOSWithSteam(Action<bool, string> onComplete = null)
        {
            try
            {
                if (Instance.IsNull())
                {
                    GameObject managerObj = new GameObject("SteamAuthManager", typeof(SteamAuthManager));
                    DontDestroyOnLoad(managerObj);
                }
                else
                {
                    // Instance valid
                }

                Instance.StartSteamLogin(onComplete);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamAuthManager] Exception in LoginToEOSWithSteam: {ex.Message}");
                onComplete?.Invoke(false, ex.Message);
            }
        }

        public void StartSteamLogin(Action<bool, string> onComplete)
        {
            try
            {
                if (!_isInitialized)
                {
                    InitializeSteamAPI();
                }
                else
                {
                    // Already initialized
                }

                if (!_isInitialized)
                {
                    string error = "Steam desktop client is not running or SteamAPI failed to initialize. Please start Steam.";
                    Debug.LogError($"[SteamAuthManager] {error}");
                    OnSteamAuthFailed?.Invoke(error);
                    onComplete?.Invoke(false, error);
                    return;
                }
                else
                {
                    // Steam initialized
                }

                _pendingLoginCallback = onComplete;

                if (_ticketTimeoutCoroutine.IsNotNull())
                {
                    StopCoroutine(_ticketTimeoutCoroutine);
                }
                else
                {
                    // No existing coroutine
                }

                _ticketTimeoutCoroutine = StartCoroutine(TicketTimeoutWatchdog(8.0f));

                uint appId = SteamUtils.GetAppID().m_AppId;
                CSteamID steamId = SteamUser.GetSteamID();
#if UNITY_EDITOR
                Debug.Log($"[SteamAuthManager] Requesting WebApi ticket for '{EOS_IDENTITY}' (AppID: {appId}, Persona: {SteamFriends.GetPersonaName()})...");
#endif
                _activeAuthTicket = SteamUser.GetAuthTicketForWebApi(EOS_IDENTITY);

                if (_activeAuthTicket == HAuthTicket.Invalid)
                {
                    Debug.LogWarning("[SteamAuthManager] GetAuthTicketForWebApi returned Invalid. Attempting legacy GetAuthSessionTicket fallback...");
                    FallbackGetAuthSessionTicket();
                }
                else
                {
                    // Ticket requested, awaiting callback
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamAuthManager] Exception in StartSteamLogin: {ex.Message}");
                NotifyFailure(ex.Message);
            }
        }

        private void HandleGetTicketForWebApiResponse(GetTicketForWebApiResponse_t callback)
        {
            try
            {
                if (_ticketTimeoutCoroutine.IsNotNull())
                {
                    StopCoroutine(_ticketTimeoutCoroutine);
                    _ticketTimeoutCoroutine = null;
                }
                else
                {
                    // Watchdog inactive
                }

                if (callback.m_eResult == EResult.k_EResultOK)
                {
                    int ticketLen = callback.m_cubTicket;
                    byte[] ticketBytes = new byte[ticketLen];
                    Array.Copy(callback.m_rgubTicket, ticketBytes, ticketLen);

                    // Convert to hex using EOS SDK native function
                    string hexToken = Epic.OnlineServices.Common.ToString(ticketBytes);
                    if (string.IsNullOrEmpty(hexToken))
                    {
                        StringBuilder sb = new StringBuilder(ticketLen * 2);
                        for (int i = 0; i < ticketLen; i++)
                        {
                            sb.AppendFormat("{0:x2}", callback.m_rgubTicket[i]);
                        }
                        hexToken = sb.ToString();
                    }
                    else
                    {
                        // Native string conversion succeeded
                    }

                    string persona = SteamFriends.GetPersonaName();

#if UNITY_EDITOR
                    Debug.Log($"<color=#55FF55>[SteamAuthManager] Received Steam WebApi ticket ({ticketLen} bytes). Authenticating with EOS...</color>");
#endif

                    OnSteamTicketAcquired?.Invoke(hexToken, persona);
                    _pendingLoginCallback?.Invoke(true, hexToken);
                    _pendingLoginCallback = null;

                    EOSSDKComponent.LoginWithSteam(hexToken, persona);
                }
                else
                {
                    string error = $"Steam GetAuthTicketForWebApi returned error: {callback.m_eResult}. Attempting session ticket fallback...";
                    Debug.LogWarning($"[SteamAuthManager] {error}");
                    FallbackGetAuthSessionTicket();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamAuthManager] Exception in HandleGetTicketForWebApiResponse: {ex.Message}");
                NotifyFailure(ex.Message);
            }
        }

        private void FallbackGetAuthSessionTicket()
        {
            try
            {
                byte[] ticketBuffer = new byte[1024];
                SteamNetworkingIdentity identity = new SteamNetworkingIdentity();
                identity.SetGenericString(EOS_IDENTITY);

                HAuthTicket ticketHandle = SteamUser.GetAuthSessionTicket(ticketBuffer, ticketBuffer.Length, out uint ticketSize, ref identity);
                if (ticketHandle != HAuthTicket.Invalid && ticketSize > 0)
                {
                    StringBuilder sb = new StringBuilder((int)ticketSize * 2);
                    for (int i = 0; i < ticketSize; i++)
                    {
                        sb.AppendFormat("{0:x2}", ticketBuffer[i]);
                    }
                    string hexToken = sb.ToString();
                    string persona = SteamFriends.GetPersonaName();

#if UNITY_EDITOR
                    Debug.Log($"<color=#55FF55>[SteamAuthManager] Fallback Steam session ticket generated ({ticketSize} bytes). Forwarding to EOS...</color>");
#endif
                    OnSteamTicketAcquired?.Invoke(hexToken, persona);
                    _pendingLoginCallback?.Invoke(true, hexToken);
                    _pendingLoginCallback = null;

                    EOSSDKComponent.LoginWithSteam(hexToken, persona);
                }
                else
                {
                    string error = "Steam session ticket fallback also failed. Please ensure Steam is fully running.";
                    Debug.LogError($"[SteamAuthManager] {error}");
                    NotifyFailure(error);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamAuthManager] Exception in FallbackGetAuthSessionTicket: {ex.Message}");
                NotifyFailure(ex.Message);
            }
        }

        private IEnumerator TicketTimeoutWatchdog(float timeoutSeconds)
        {
            yield return new WaitForSecondsRealtime(timeoutSeconds);

            Debug.LogWarning("[SteamAuthManager] WebApi ticket request timed out after 8s. Trying session ticket fallback...");
            FallbackGetAuthSessionTicket();
        }

        private void NotifyFailure(string reason)
        {
            try
            {
                if (_ticketTimeoutCoroutine.IsNotNull())
                {
                    StopCoroutine(_ticketTimeoutCoroutine);
                    _ticketTimeoutCoroutine = null;
                }
                else
                {
                    // No active watchdog
                }

                OnSteamAuthFailed?.Invoke(reason);
                _pendingLoginCallback?.Invoke(false, reason);
                _pendingLoginCallback = null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamAuthManager] Exception in NotifyFailure: {ex.Message}");
            }
        }
    }
}
