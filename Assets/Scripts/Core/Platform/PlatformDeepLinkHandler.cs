using System;
using System.Collections.Generic;
using UnityEngine;

namespace JustAGame.Core.Platform
{
    /// <summary>
    /// Handles deep link activations (e.g. justagame://join?host=PUID or justagame://auth?type=Steam).
    /// Enables seamless one-click joining from external websites, Discord, or desktop launchers.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("JustAGame/Platform/Platform Deep Link Handler")]
    public class PlatformDeepLinkHandler : MonoBehaviour
    {
        public static PlatformDeepLinkHandler Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            try
            {
                if (Instance.IsNull())
                {
                    var existing = UnityEngine.Object.FindFirstObjectByType<PlatformDeepLinkHandler>();
                    if (existing.IsNull())
                    {
                        var go = new GameObject("[PlatformDeepLinkHandler]");
                        go.AddComponent<PlatformDeepLinkHandler>();
                    }
                    else
                    {
                        // Found in scene
                    }
                }
                else
                {
                    // Already instantiated
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlatformDeepLinkHandler] Exception in AutoInitialize: {ex.Message}");
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
                    Application.deepLinkActivated += OnDeepLinkActivated;

                    if (!string.IsNullOrEmpty(Application.absoluteURL))
                    {
                        OnDeepLinkActivated(Application.absoluteURL);
                    }
                    else
                    {
                        // No deep link at cold start
                    }
                }
                else
                {
                    if (!ReferenceEquals(Instance, this))
                    {
                        Destroy(gameObject);
                        return;
                    }
                    else
                    {
                        // Existing instance
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlatformDeepLinkHandler] Exception in Awake: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (ReferenceEquals(Instance, this))
                {
                    Application.deepLinkActivated -= OnDeepLinkActivated;
                    Instance = null;
                }
                else
                {
                    // Secondary instance destroyed
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlatformDeepLinkHandler] Exception in OnDestroy: {ex.Message}");
            }
        }

        private void OnDeepLinkActivated(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    return;
                }
                else
                {
                    // Process deep link URL
                }

#if UNITY_EDITOR
                Debug.Log($"[PlatformDeepLinkHandler] Deep link activated: {url}");
#endif
                var uri = new Uri(url);
                string hostAction = uri.Host.ToLowerInvariant();
                var queryParams = ParseQueryString(uri.Query);

                if (hostAction == "join")
                {
                    if (queryParams.TryGetValue("host", out string hostPuid) && !string.IsNullOrEmpty(hostPuid))
                    {
                        if (GamePlatform.Auth.IsLoggedIn)
                        {
                            GamePlatform.Network.JoinHost(hostPuid);
                        }
                        else
                        {
                            Action<PlatformAuthInfo> onLoginJoin = null;
                            onLoginJoin = (info) =>
                            {
                                GamePlatform.Auth.OnLoginSuccess -= onLoginJoin;
                                GamePlatform.Network.JoinHost(hostPuid);
                            };
                            GamePlatform.Auth.OnLoginSuccess += onLoginJoin;
                            GamePlatform.Auth.LoginWithDeviceId();
                        }
                    }
                    else
                    {
                        // Missing host parameter
                    }
                }
                else if (hostAction == "host")
                {
                    if (GamePlatform.Auth.IsLoggedIn)
                    {
                        GamePlatform.Network.StartHost();
                    }
                    else
                    {
                        Action<PlatformAuthInfo> onLoginHost = null;
                        onLoginHost = (info) =>
                        {
                            GamePlatform.Auth.OnLoginSuccess -= onLoginHost;
                            GamePlatform.Network.StartHost();
                        };
                        GamePlatform.Auth.OnLoginSuccess += onLoginHost;
                        GamePlatform.Auth.LoginWithDeviceId();
                    }
                }
                else
                {
                    // Custom deep link action
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlatformDeepLinkHandler] Exception in OnDeepLinkActivated: {ex.Message}");
            }
        }

        private Dictionary<string, string> ParseQueryString(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (string.IsNullOrEmpty(query))
                {
                    return dict;
                }
                else
                {
                    // Parse query
                }

                string trimmed = query.TrimStart('?');
                string[] pairs = trimmed.Split('&');
                for (int i = 0; i < pairs.Length; i++)
                {
                    string pair = pairs[i];
                    int eqIndex = pair.IndexOf('=');
                    if (eqIndex > 0 && eqIndex < pair.Length - 1)
                    {
                        string key = pair.Substring(0, eqIndex);
                        string value = Uri.UnescapeDataString(pair.Substring(eqIndex + 1));
                        dict[key] = value;
                    }
                    else
                    {
                        // Invalid key-value pair
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlatformDeepLinkHandler] Exception in ParseQueryString: {ex.Message}");
            }

            return dict;
        }
    }
}
