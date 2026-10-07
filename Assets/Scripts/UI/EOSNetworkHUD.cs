using System;
using UnityEngine;
using Mirror;
using JustAGame.Core.Network;
using JustAGame.Pooling;

namespace JustAGame.UI
{
    /// <summary>
    /// Professional In-Game HUD for testing and managing Epic Online Services (EOS) P2P connections.
    /// Provides one-click Host, Join via Epic Product ID, Clipboard copying, and Status diagnostics.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("JustAGame/UI/EOS Network HUD")]
    public class EOSNetworkHUD : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool showHUD = true;
        [SerializeField] private int offsetX = 15;
        [SerializeField] private int offsetY = 15;
        [SerializeField] private int panelWidth = 320;

        public static EOSNetworkHUD Instance { get; private set; }
        public bool ShowHUD { get => showHUD; set => showHUD = value; }

        private string _targetHostId = string.Empty;
        private string _statusMessage = string.Empty;
        private string _authProfileName = "Player1";
        private string _devAuthPortStr = "7878";
        private EOSNetworkManagerBridge _bridge;

        private void Awake()
        {
            try
            {
                Instance = this;
                ResolveBridge();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in Awake: {ex.Message}");
            }
        }

        private void OnEnable()
        {
            try
            {
                EOSNetworkManagerBridge.OnEosHostStarted += HandleHostStarted;
                EOSNetworkManagerBridge.OnEosClientStarted += HandleClientStarted;
                EOSNetworkManagerBridge.OnEosSessionStopped += HandleSessionStopped;
                EOSNetworkManagerBridge.OnEosError += HandleError;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in OnEnable: {ex.Message}");
            }
        }

        private void OnDisable()
        {
            try
            {
                EOSNetworkManagerBridge.OnEosHostStarted -= HandleHostStarted;
                EOSNetworkManagerBridge.OnEosClientStarted -= HandleClientStarted;
                EOSNetworkManagerBridge.OnEosSessionStopped -= HandleSessionStopped;
                EOSNetworkManagerBridge.OnEosError -= HandleError;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in OnDisable: {ex.Message}");
            }
        }

        private void ResolveBridge()
        {
            try
            {
                if (_bridge.IsNull())
                {
                    _bridge = EOSNetworkManagerBridge.Instance;
                    if (_bridge.IsNull())
                    {
                        _bridge = FindFirstObjectByType<EOSNetworkManagerBridge>();
                    }
                    else
                    {
                        // Bridge resolved from singleton
                    }
                }
                else
                {
                    // Bridge already cached
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in ResolveBridge: {ex.Message}");
            }
        }

        private void OnGUI()
        {
            try
            {
                if (EpicTransport.EOSSDKComponent.Initialized && !showHUD)
                {
                    showHUD = true;
                }
                else
                {
                    // Visibility maintained
                }

                if (!showHUD || !EpicTransport.EOSSDKComponent.Initialized)
                {
                    return;
                }
                else
                {
                    ResolveBridge();

                    GUILayout.BeginArea(new Rect(offsetX, offsetY, panelWidth, 420), GUI.skin.box);
                    GUILayout.Label("<b><size=13>EOS P2P Multiplayer HUD</size></b>");
                    GUILayout.Space(4);

                    DrawEosStatus();
                    GUILayout.Space(8);

                    if (NetworkServer.active || NetworkClient.active)
                    {
                        DrawActiveSession();
                    }
                    else
                    {
                        DrawOfflineControls();
                    }

                    if (!string.IsNullOrEmpty(_statusMessage))
                    {
                        GUILayout.Space(6);
                        GUILayout.Label($"<color=yellow>{_statusMessage}</color>");
                    }
                    else
                    {
                        // No status or error message
                    }

                    GUILayout.EndArea();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in OnGUI: {ex.Message}");
            }
        }

        private void DrawEosStatus()
        {
            try
            {
                bool isReady = _bridge.IsNotNull() && _bridge.IsEosReady;
                if (isReady)
                {
                    GUILayout.Label("<color=#80FF80>● EOS Status: Ready</color>");
                    string localId = _bridge.LocalProductId;
                    if (!string.IsNullOrEmpty(localId))
                    {
                        GUILayout.Label($"<size=10>Local Product ID:\n{localId}</size>");
                    }
                    else
                    {
                        GUILayout.Label("<size=10>Local Product ID: Acquiring...</size>");
                    }
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Profile:", GUILayout.Width(50));
                    _authProfileName = GUILayout.TextField(_authProfileName);
                    GUILayout.Label("Port:", GUILayout.Width(35));
                    _devAuthPortStr = GUILayout.TextField(_devAuthPortStr, GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Login (DevAuth)"))
                    {
                        string cred = string.IsNullOrEmpty(_authProfileName) ? "Player1" : _authProfileName.Trim();
                        uint port = uint.TryParse(_devAuthPortStr, out uint p) ? p : 7878;
                        _statusMessage = $"Logging into DevAuth as '{cred}' on port {port}...";
                        EpicTransport.EOSSDKComponent.LoginWithDevAuth(cred, port);
                    }
                    else
                    {
                        // Button idle
                    }

                    if (GUILayout.Button("Login (Epic Account)"))
                    {
                        _statusMessage = "Opening Epic Account Portal in browser...";
                        EpicTransport.EOSSDKComponent.LoginWithEpicAccount();
                    }
                    else
                    {
                        // Button idle
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Login with Steam"))
                    {
                        _statusMessage = "Requesting Steam WebApi Auth Ticket...";
                        JustAGame.Core.Network.SteamAuthManager.LoginToEOSWithSteam((bool success, string error) =>
                        {
                            if (!success)
                            {
                                _statusMessage = $"<color=red>Steam Auth Error: {error}</color>";
                            }
                            else
                            {
                                _statusMessage = "Steam Ticket acquired! Connecting to EOS...";
                            }
                        });
                    }
                    else
                    {
                        // Button idle
                    }

                    if (GUILayout.Button("Quick Guest (Device ID)"))
                    {
                        string cred = string.IsNullOrEmpty(_authProfileName) ? "User" : _authProfileName.Trim();
                        _statusMessage = $"Logging in via Device ID...";
                        EpicTransport.EOSSDKComponent.LoginWithDeviceId(cred);
                    }
                    else
                    {
                        // Button idle
                    }
                    GUILayout.EndHorizontal();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in DrawEosStatus: {ex.Message}");
            }
        }

        private void DrawOfflineControls()
        {
            try
            {
                bool isReady = _bridge.IsNotNull() && _bridge.IsEosReady;
                GUI.enabled = isReady;

                if (GUILayout.Button("Host Game (EOS P2P)", GUILayout.Height(30)))
                {
                    if (_bridge.IsNotNull())
                    {
                        _bridge.StartEosHost();
                    }
                    else
                    {
                        _statusMessage = "Bridge component not found.";
                    }
                }
                else
                {
                    // Button idle
                }

                GUILayout.Space(6);
                GUILayout.Label("Join Remote EOS Host:");
                _targetHostId = GUILayout.TextField(_targetHostId).Trim();

                if (!string.IsNullOrEmpty(_targetHostId))
                {
                    if (_targetHostId.Length != 32)
                    {
                        GUILayout.Label($"<color=#FFAA00><size=11>⚠️ Length: {_targetHostId.Length}/32 (must be exactly 32 chars)</size></color>");
                    }
                    else
                    {
                        GUILayout.Label("<color=#80FF80><size=11>✓ Valid 32-character Product ID format</size></color>");
                    }
                }
                else
                {
                    // Empty target host field
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Paste ID"))
                {
                    _targetHostId = GUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
                }
                else
                {
                    // Paste button idle
                }

                if (GUILayout.Button("Connect Client", GUILayout.Height(24)))
                {
                    if (_bridge.IsNotNull())
                    {
                        _bridge.StartEosClient(_targetHostId);
                    }
                    else
                    {
                        _statusMessage = "Bridge component not found.";
                    }
                }
                else
                {
                    // Connect button idle
                }
                GUILayout.EndHorizontal();

                GUI.enabled = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in DrawOfflineControls: {ex.Message}");
            }
        }

        private void DrawActiveSession()
        {
            try
            {
                if (NetworkServer.active && NetworkClient.isConnected)
                {
                    GUILayout.Label("<color=#80FF80><b>Session: Host (Server + Client)</b></color>");
                }
                else if (NetworkClient.isConnected)
                {
                    GUILayout.Label("<color=#80FFFF><b>Session: Connected Client</b></color>");
                }
                else if (NetworkServer.active)
                {
                    GUILayout.Label("<color=#FFD700><b>Session: Dedicated Server</b></color>");
                }
                else
                {
                    GUILayout.Label("<b>Session: Connecting...</b>");
                }

                if (_bridge.IsNotNull())
                {
                    string localId = _bridge.LocalProductId;
                    if (!string.IsNullOrEmpty(localId))
                    {
                        if (GUILayout.Button("Copy My EOS ID to Clipboard"))
                        {
                            _bridge.CopyHostAddressToClipboard();
                            _statusMessage = "Host ID copied to clipboard!";
                        }
                        else
                        {
                            // Copy button idle
                        }
                    }
                    else
                    {
                        // No local ID available
                    }
                }
                else
                {
                    // Bridge missing
                }

                GUILayout.Space(6);
                if (GUILayout.Button("Disconnect", GUILayout.Height(28)))
                {
                    if (_bridge.IsNotNull())
                    {
                        _bridge.StopSession();
                    }
                    else
                    {
                        _statusMessage = "Bridge component not found.";
                    }
                }
                else
                {
                    // Disconnect button idle
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in DrawActiveSession: {ex.Message}");
            }
        }

        private void HandleHostStarted(string hostId)
        {
            try
            {
                _statusMessage = $"Hosting via EOS! ID: {hostId}";
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in HandleHostStarted: {ex.Message}");
            }
        }

        private void HandleClientStarted(string hostId)
        {
            try
            {
                _statusMessage = $"Connecting to {hostId}...";
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in HandleClientStarted: {ex.Message}");
            }
        }

        private void HandleSessionStopped()
        {
            try
            {
                _statusMessage = "Session disconnected.";
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in HandleSessionStopped: {ex.Message}");
            }
        }

        private void HandleError(string error)
        {
            try
            {
                _statusMessage = $"Error: {error}";
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkHUD] Exception in HandleError: {ex.Message}");
            }
        }
    }
}
