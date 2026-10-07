using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mirror;
using JustAGame.Core.Network;
using JustAGame.Pooling;
using UnityEngine;

namespace JustAGame.Core.Platform
{
    /// <summary>
    /// Production implementation of IPlatformNetworkService and INetworkManager.
    /// Manages Mirror P2P session lifecycles, host initiation, client connections, and peer authorization.
    /// Bridges both the internal platform API and the external-facing INetworkManager contract.
    /// </summary>
    public sealed class EOSPlatformNetworkService : IPlatformNetworkService, INetworkManager
    {
        public bool IsHost => NetworkServer.active && NetworkClient.isConnected;
        public bool IsClient => NetworkClient.isConnected && !NetworkServer.active;
        public bool IsSessionActive => NetworkServer.active || NetworkClient.active;

        public string HostAddress
        {
            get
            {
                if (EOSNetworkManagerBridge.Instance.IsNotNull())
                {
                    return EOSNetworkManagerBridge.Instance.LocalProductId;
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        public int ConnectedPeerCount
        {
            get
            {
                if (NetworkServer.active)
                {
                    return NetworkServer.connections.Count;
                }
                else
                {
                    return NetworkClient.isConnected ? 1 : 0;
                }
            }
        }

        public bool UseActiveWhitelist
        {
            get
            {
                if (EOSNetworkManagerBridge.Instance.IsNotNull())
                {
                    return EOSNetworkManagerBridge.Instance.UseActiveWhitelist;
                }
                else
                {
                    return false;
                }
            }
            set
            {
                if (EOSNetworkManagerBridge.Instance.IsNotNull())
                {
                    EOSNetworkManagerBridge.Instance.UseActiveWhitelist = value;
                }
                else
                {
                    // Bridge not yet initialized
                }
            }
        }

        public event Action<string> OnHostStarted;
        public event Action<string> OnClientStarted;
        public event Action OnClientConnected;
        public event Action<string> OnClientDisconnected;
        public event Action OnSessionStopped;
        public event Action<string> OnNetworkError;

        public EOSPlatformNetworkService()
        {
            try
            {
                EOSNetworkManagerBridge.OnEosHostStarted += HandleHostStarted;
                EOSNetworkManagerBridge.OnEosClientStarted += HandleClientStarted;
                EOSNetworkManagerBridge.OnEosSessionStopped += HandleSessionStopped;
                EOSNetworkManagerBridge.OnEosError += HandleNetworkError;

                GameNetworkManager.OnClientConnectedToServer += HandleClientConnected;
                GameNetworkManager.OnClientDisconnectedFromServer += HandleClientDisconnected;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in constructor: {ex.Message}");
            }
        }

        // ───────────────────────────────────────────────
        // IPlatformNetworkService Implementation
        // ───────────────────────────────────────────────

        public bool StartHost()
        {
            try
            {
                var bridge = GetBridge();
                if (bridge.IsNull())
                {
                    string error = "EOSNetworkManagerBridge instance not found in scene.";
                    Debug.LogError($"[EOSPlatformNetworkService] {error}");
                    OnNetworkError?.Invoke(error);
                    return false;
                }
                else
                {
                    return bridge.StartEosHost();
                }
            }
            catch (Exception ex)
            {
                string error = $"Exception in StartHost: {ex.Message}";
                Debug.LogError($"[EOSPlatformNetworkService] {error}");
                OnNetworkError?.Invoke(error);
                return false;
            }
        }

        public bool JoinHost(string hostProductUserId)
        {
            try
            {
                var bridge = GetBridge();
                if (bridge.IsNull())
                {
                    string error = "EOSNetworkManagerBridge instance not found in scene.";
                    Debug.LogError($"[EOSPlatformNetworkService] {error}");
                    OnNetworkError?.Invoke(error);
                    return false;
                }
                else
                {
                    return bridge.StartEosClient(hostProductUserId);
                }
            }
            catch (Exception ex)
            {
                string error = $"Exception in JoinHost: {ex.Message}";
                Debug.LogError($"[EOSPlatformNetworkService] {error}");
                OnNetworkError?.Invoke(error);
                return false;
            }
        }

        public void StopSession()
        {
            try
            {
                var bridge = GetBridge();
                if (bridge.IsNotNull())
                {
                    bridge.StopSession();
                }
                else
                {
                    if (NetworkServer.active && NetworkClient.isConnected)
                    {
                        NetworkManager.singleton.StopHost();
                    }
                    else if (NetworkClient.isConnected || NetworkClient.active)
                    {
                        NetworkManager.singleton.StopClient();
                    }
                    else if (NetworkServer.active)
                    {
                        NetworkManager.singleton.StopServer();
                    }
                    else
                    {
                        // No active session
                    }

                    OnSessionStopped?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in StopSession: {ex.Message}");
            }
        }

        public void AddAuthorizedPeer(string productUserId)
        {
            try
            {
                var bridge = GetBridge();
                if (bridge.IsNotNull())
                {
                    bridge.AddAuthorizedPeer(productUserId);
                }
                else
                {
                    // Bridge not available
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in AddAuthorizedPeer: {ex.Message}");
            }
        }

        public void RemoveAuthorizedPeer(string productUserId)
        {
            try
            {
                var bridge = GetBridge();
                if (bridge.IsNotNull())
                {
                    bridge.RemoveAuthorizedPeer(productUserId);
                }
                else
                {
                    // Bridge not available
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in RemoveAuthorizedPeer: {ex.Message}");
            }
        }

        public bool IsPeerAuthorized(string productUserId)
        {
            try
            {
                var bridge = GetBridge();
                if (bridge.IsNotNull())
                {
                    return bridge.IsPeerAuthorized(productUserId);
                }
                else
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in IsPeerAuthorized: {ex.Message}");
                return false;
            }
        }

        public void ClearAuthorizedPeers()
        {
            try
            {
                // Cleared via disable or custom bridge logic
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in ClearAuthorizedPeers: {ex.Message}");
            }
        }

        public bool CopyHostAddressToClipboard()
        {
            try
            {
                var bridge = GetBridge();
                if (bridge.IsNotNull())
                {
                    return bridge.CopyHostAddressToClipboard();
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in CopyHostAddressToClipboard: {ex.Message}");
                return false;
            }
        }

        // ───────────────────────────────────────────────
        // INetworkManager Implementation
        // ───────────────────────────────────────────────

        /// <inheritdoc />
        public void StartLocal()
        {
            try
            {
                var nm = GetNetworkManager();
                if (nm.IsNull())
                {
                    string error = "GameNetworkManager not found in scene. Cannot start local host.";
                    Debug.LogError($"[EOSPlatformNetworkService] {error}");
                    OnNetworkError?.Invoke(error);
                }
                else
                {
                    if (NetworkServer.active || NetworkClient.active)
                    {
                        Debug.LogWarning("[EOSPlatformNetworkService] StartLocal ignored: a session is already active.");
                    }
                    else
                    {
                        nm.networkAddress = "localhost";
                        nm.StartHost();

#if UNITY_EDITOR
                        Debug.Log("[EOSPlatformNetworkService] Local host started on localhost.");
#endif
                    }
                }
            }
            catch (Exception ex)
            {
                string error = $"Exception in StartLocal: {ex.Message}";
                Debug.LogError($"[EOSPlatformNetworkService] {error}");
                OnNetworkError?.Invoke(error);
            }
        }

        /// <inheritdoc />
        public void ConnectLocal(string ip)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ip))
                {
                    string error = "ConnectLocal failed: IP address cannot be empty.";
                    Debug.LogError($"[EOSPlatformNetworkService] {error}");
                    OnNetworkError?.Invoke(error);
                }
                else
                {
                    var nm = GetNetworkManager();
                    if (nm.IsNull())
                    {
                        string error = "GameNetworkManager not found in scene. Cannot connect locally.";
                        Debug.LogError($"[EOSPlatformNetworkService] {error}");
                        OnNetworkError?.Invoke(error);
                    }
                    else
                    {
                        if (NetworkServer.active || NetworkClient.active)
                        {
                            Debug.LogWarning("[EOSPlatformNetworkService] ConnectLocal ignored: a session is already active.");
                        }
                        else
                        {
                            nm.networkAddress = ip.Trim();
                            nm.StartClient();

#if UNITY_EDITOR
                            Debug.Log($"[EOSPlatformNetworkService] Connecting locally to {ip.Trim()}...");
#endif
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string error = $"Exception in ConnectLocal: {ex.Message}";
                Debug.LogError($"[EOSPlatformNetworkService] {error}");
                OnNetworkError?.Invoke(error);
            }
        }

        /// <inheritdoc />
        public Task<NetworkStartResult> StartRemote()
        {
            try
            {
                var bridge = GetBridge();
                if (bridge.IsNull())
                {
                    Debug.LogError("[EOSPlatformNetworkService] StartRemote failed: EOSNetworkManagerBridge not found.");
                    return Task.FromResult(NetworkStartResult.MissingDependency);
                }
                else
                {
                    if (!bridge.IsEosReady)
                    {
                        Debug.LogError("[EOSPlatformNetworkService] StartRemote failed: EOS SDK not initialized.");
                        return Task.FromResult(NetworkStartResult.NotInitialized);
                    }
                    else
                    {
                        if (NetworkServer.active || NetworkClient.active)
                        {
                            Debug.LogWarning("[EOSPlatformNetworkService] StartRemote ignored: session already active.");
                            return Task.FromResult(NetworkStartResult.AlreadyActive);
                        }
                        else
                        {
                            bool started = bridge.StartEosHost();
                            if (started)
                            {
                                return Task.FromResult(NetworkStartResult.Success);
                            }
                            else
                            {
                                return Task.FromResult(NetworkStartResult.InternalError);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in StartRemote: {ex.Message}");
                return Task.FromResult(NetworkStartResult.InternalError);
            }
        }

        /// <inheritdoc />
        public Task<NetworkStartResult> JoinRemote(string code)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 32)
                {
                    Debug.LogError("[EOSPlatformNetworkService] JoinRemote failed: code is empty or does not match 32-character EOS Product User ID format.");
                    return Task.FromResult(NetworkStartResult.InvalidCode);
                }
                else
                {
                    var bridge = GetBridge();
                    if (bridge.IsNull())
                    {
                        Debug.LogError("[EOSPlatformNetworkService] JoinRemote failed: EOSNetworkManagerBridge not found.");
                        return Task.FromResult(NetworkStartResult.MissingDependency);
                    }
                    else
                    {
                        if (!bridge.IsEosReady)
                        {
                            Debug.LogError("[EOSPlatformNetworkService] JoinRemote failed: EOS SDK not initialized.");
                            return Task.FromResult(NetworkStartResult.NotInitialized);
                        }
                        else
                        {
                            if (NetworkServer.active || NetworkClient.active)
                            {
                                Debug.LogWarning("[EOSPlatformNetworkService] JoinRemote ignored: session already active.");
                                return Task.FromResult(NetworkStartResult.AlreadyActive);
                            }
                            else
                            {
                                bool joined = bridge.StartEosClient(code.Trim());
                                if (joined)
                                {
                                    return Task.FromResult(NetworkStartResult.Success);
                                }
                                else
                                {
                                    return Task.FromResult(NetworkStartResult.InternalError);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in JoinRemote: {ex.Message}");
                return Task.FromResult(NetworkStartResult.InternalError);
            }
        }

        /// <inheritdoc />
        public string GetCode()
        {
            try
            {
                var bridge = GetBridge();
                if (bridge.IsNotNull() && bridge.IsEosReady)
                {
                    return bridge.LocalProductId;
                }
                else
                {
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in GetCode: {ex.Message}");
                return string.Empty;
            }
        }

        // ───────────────────────────────────────────────
        // Internal Helpers
        // ───────────────────────────────────────────────

        private EOSNetworkManagerBridge GetBridge()
        {
            try
            {
                if (EOSNetworkManagerBridge.Instance.IsNotNull())
                {
                    return EOSNetworkManagerBridge.Instance;
                }
                else
                {
                    return UnityEngine.Object.FindFirstObjectByType<EOSNetworkManagerBridge>();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in GetBridge: {ex.Message}");
                return null;
            }
        }

        private GameNetworkManager GetNetworkManager()
        {
            try
            {
                if (NetworkManager.singleton.IsNotNull())
                {
                    return NetworkManager.singleton as GameNetworkManager;
                }
                else
                {
                    return UnityEngine.Object.FindFirstObjectByType<GameNetworkManager>();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in GetNetworkManager: {ex.Message}");
                return null;
            }
        }

        // ───────────────────────────────────────────────
        // Event Handlers
        // ───────────────────────────────────────────────

        private void HandleHostStarted(string localPuid)
        {
            try
            {
                OnHostStarted?.Invoke(localPuid);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in HandleHostStarted: {ex.Message}");
            }
        }

        private void HandleClientStarted(string targetHost)
        {
            try
            {
                OnClientStarted?.Invoke(targetHost);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in HandleClientStarted: {ex.Message}");
            }
        }

        private void HandleSessionStopped()
        {
            try
            {
                OnSessionStopped?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in HandleSessionStopped: {ex.Message}");
            }
        }

        private void HandleNetworkError(string error)
        {
            try
            {
                OnNetworkError?.Invoke(error);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in HandleNetworkError: {ex.Message}");
            }
        }

        private void HandleClientConnected()
        {
            try
            {
                OnClientConnected?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in HandleClientConnected: {ex.Message}");
            }
        }

        private void HandleClientDisconnected()
        {
            try
            {
                OnClientDisconnected?.Invoke("Disconnected from remote host.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformNetworkService] Exception in HandleClientDisconnected: {ex.Message}");
            }
        }
    }
}
