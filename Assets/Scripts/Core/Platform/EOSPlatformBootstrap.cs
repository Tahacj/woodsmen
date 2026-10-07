using System;
using System.Collections;
using JustAGame.Pooling;
using UnityEngine;

namespace JustAGame.Core.Platform
{
    /// <summary>
    /// Automates platform bootstrapping when launched via external interfaces, launchers, or CLI.
    /// Handles automated headless/UI authentication and automatic host/join flows.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("JustAGame/Platform/EOS Platform Bootstrap")]
    public class EOSPlatformBootstrap : MonoBehaviour
    {
        [Header("Bootstrap Configuration")]
        [Tooltip("If true, automatically checks and executes CLI arguments upon startup.")]
        [SerializeField] private bool processCommandLineArgsOnStart = true;

        [Tooltip("Optional reference to login canvas to hide during automated launcher sessions.")]
        [SerializeField] private GameObject loginCanvasRoot;

        private PlatformCommandLineArgs _args;
        private bool _bootstrapExecuted = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCheckCommandLine()
        {
            try
            {
                var args = PlatformCommandLineArgs.ParseCurrent();
                if (args.HasAutoLogin || args.HasAutoHost || args.HasAutoJoin || args.HideUI)
                {
                    var existing = UnityEngine.Object.FindFirstObjectByType<EOSPlatformBootstrap>();
                    if (existing.IsNull())
                    {
                        var go = new GameObject("[EOSPlatformBootstrap]");
                        var bootstrap = go.AddComponent<EOSPlatformBootstrap>();
                        DontDestroyOnLoad(go);
                        bootstrap.ExecuteBootstrap();
                    }
                    else
                    {
                        // Component already in scene, will execute via Start()
                    }
                }
                else
                {
                    // No CLI automation arguments passed, normal interactive mode
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformBootstrap] Exception in AutoCheckCommandLine: {ex.Message}");
            }
        }

        private void Start()
        {
            try
            {
                if (processCommandLineArgsOnStart && !_bootstrapExecuted)
                {
                    ExecuteBootstrap();
                }
                else
                {
                    // Bootstrap disabled or already executed
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformBootstrap] Exception in Start: {ex.Message}");
            }
        }

        public void ExecuteBootstrap()
        {
            try
            {
                _bootstrapExecuted = true;
                _args = PlatformCommandLineArgs.ParseCurrent();

                if (_args.HideUI && loginCanvasRoot.IsNotNull())
                {
                    loginCanvasRoot.SetActive(false);
                }
                else
                {
                    // UI remains in default state
                }

                if (_args.WhitelistPuids.Count > 0)
                {
                    GamePlatform.Network.UseActiveWhitelist = true;
                    for (int i = 0; i < _args.WhitelistPuids.Count; i++)
                    {
                        GamePlatform.Network.AddAuthorizedPeer(_args.WhitelistPuids[i]);
                    }
                }
                else
                {
                    // No CLI whitelist specified
                }

                if (_args.HasAutoLogin)
                {
                    StartCoroutine(ExecuteAutoLoginFlowRoutine());
                }
                else
                {
                    // Interactive manual login mode
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformBootstrap] Exception in ExecuteBootstrap: {ex.Message}");
            }
        }

        private IEnumerator ExecuteAutoLoginFlowRoutine()
        {
            // Give 1 frame for underlying singletons to bind
            yield return null;

            Action<PlatformAuthInfo> onLoginSuccessHandler = null;
            onLoginSuccessHandler = (info) =>
            {
                try
                {
                    GamePlatform.Auth.OnLoginSuccess -= onLoginSuccessHandler;

                    if (_args.HasAutoHost)
                    {
                        StartCoroutine(DelayedAutoHostRoutine());
                    }
                    else if (_args.HasAutoJoin && !string.IsNullOrEmpty(_args.TargetHostPuid))
                    {
                        StartCoroutine(DelayedAutoJoinRoutine(_args.TargetHostPuid));
                    }
                    else
                    {
                        // No network auto-action requested
                    }
                }
                catch (Exception cbEx)
                {
                    Debug.LogError($"[EOSPlatformBootstrap] Exception in auto login callback: {cbEx.Message}");
                }
            };

            GamePlatform.Auth.OnLoginSuccess += onLoginSuccessHandler;

            switch (_args.AutoLoginType)
            {
                case PlatformAuthType.Steam:
                    GamePlatform.Auth.LoginWithSteam();
                    break;

                case PlatformAuthType.DevAuthTool:
                    GamePlatform.Auth.LoginWithDevAuth(_args.DevAuthServer, _args.DevAuthCredential);
                    break;

                case PlatformAuthType.EpicAccountPortal:
                    GamePlatform.Auth.LoginWithEpicAccount();
                    break;

                case PlatformAuthType.DeviceId:
                default:
                    string name = !string.IsNullOrEmpty(_args.PlayerName) ? _args.PlayerName : "LauncherUser";
                    GamePlatform.Auth.LoginWithDeviceId(name);
                    break;
            }
        }

        private IEnumerator DelayedAutoHostRoutine()
        {
            // Allow 0.2s for platform connection state to stabilize
            yield return new WaitForSeconds(0.2f);
            GamePlatform.Network.StartHost();
        }

        private IEnumerator DelayedAutoJoinRoutine(string hostPuid)
        {
            yield return new WaitForSeconds(0.2f);
            GamePlatform.Network.JoinHost(hostPuid);
        }
    }
}
