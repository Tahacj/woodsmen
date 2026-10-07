using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using EpicTransport;
using JustAGame.Pooling;

namespace JustAGame.Core.Network
{
    /// <summary>
    /// Professional bridge linking Epic Online Services (EOS) transport with Mirror's GameNetworkManager.
    /// Manages host registration, client P2P connection via Epic Product User IDs, and session lifecycles.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("JustAGame/Network/EOS Network Manager Bridge")]
    public class EOSNetworkManagerBridge : MonoBehaviour
    {
        public static EOSNetworkManagerBridge Instance { get; private set; }

        public static event Action<string> OnEosHostStarted;
        public static event Action<string> OnEosClientStarted;
        public static event Action OnEosSessionStopped;
        public static event Action<string> OnEosError;

        [Header("References")]
        [SerializeField] private GameNetworkManager networkManager;
        [SerializeField] private EosTransport eosTransport;
        [SerializeField] private EOSNetworkAuthenticator authenticator;

        [Header("Security Settings")]
        [SerializeField] private bool useActiveWhitelist = false;
        private readonly HashSet<string> _authorizedPeers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool UseActiveWhitelist { get => useActiveWhitelist; set => useActiveWhitelist = value; }

        public bool IsEosReady => EOSSDKComponent.Initialized;
        public string LocalProductId => EOSSDKComponent.Initialized ? EOSSDKComponent.LocalUserProductIdString : string.Empty;
        public bool IsSessionActive => NetworkServer.active || NetworkClient.active;

        public void AddAuthorizedPeer(string productUserId)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(productUserId))
                {
                    _authorizedPeers.Add(productUserId.Trim());
                }
                else
                {
                    // Empty peer ignored
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in AddAuthorizedPeer: {ex.Message}");
            }
        }

        public void RemoveAuthorizedPeer(string productUserId)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(productUserId))
                {
                    _authorizedPeers.Remove(productUserId.Trim());
                }
                else
                {
                    // Empty peer ignored
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in RemoveAuthorizedPeer: {ex.Message}");
            }
        }

        public bool IsPeerAuthorized(string productUserId)
        {
            try
            {
                if (!useActiveWhitelist)
                {
                    return true;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(productUserId))
                    {
                        return false;
                    }
                    else
                    {
                        return _authorizedPeers.Contains(productUserId.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in IsPeerAuthorized: {ex.Message}");
                return false;
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
                }
                else
                {
                    if (Instance != this)
                    {
                        Destroy(gameObject);
                        return;
                    }
                    else
                    {
                        // Instance already pointing to this
                    }
                }

                ResolveReferences();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in Awake: {ex.Message}");
            }
        }

        private void OnEnable()
        {
            try
            {
                GameNetworkManager.OnClientDisconnectedFromServer += HandleClientDisconnectedFromServer;
                GameNetworkManager.OnClientTransportError += HandleClientTransportError;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in OnEnable: {ex.Message}");
            }
        }

        private void OnDisable()
        {
            try
            {
                GameNetworkManager.OnClientDisconnectedFromServer -= HandleClientDisconnectedFromServer;
                GameNetworkManager.OnClientTransportError -= HandleClientTransportError;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in OnDisable: {ex.Message}");
            }
        }

        private void HandleClientDisconnectedFromServer()
        {
            try
            {
                OnEosSessionStopped?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in HandleClientDisconnectedFromServer: {ex.Message}");
            }
        }

        private void HandleClientTransportError(TransportError error, string reason)
        {
            try
            {
                string msg = $"Transport error: {error} ({reason})";
                Debug.LogError($"[EOSNetworkManagerBridge] {msg}");
                OnEosError?.Invoke(msg);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in HandleClientTransportError: {ex.Message}");
            }
        }

        private void Start()
        {
            try
            {
                ResolveReferences();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in Start: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (Instance == this)
                {
                    Instance = null;
                }
                else
                {
                    // Secondary instance destroyed
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in OnDestroy: {ex.Message}");
            }
        }

        public void ResolveReferences()
        {
            try
            {
                if (networkManager.IsNull())
                {
                    networkManager = this.GetComponentOrNull<GameNetworkManager>();
                    if (networkManager.IsNull())
                    {
                        networkManager = FindFirstObjectByType<GameNetworkManager>();
                    }
                    else
                    {
                        // Found on current GameObject
                    }
                }
                else
                {
                    // NetworkManager pre-assigned
                }

                if (eosTransport.IsNull())
                {
                    eosTransport = this.GetComponentOrNull<EosTransport>();
                    if (eosTransport.IsNull())
                    {
                        eosTransport = FindFirstObjectByType<EosTransport>();
                    }
                    else
                    {
                        // Found on current GameObject
                    }
                }
                else
                {
                    // EosTransport pre-assigned
                }

                if (authenticator.IsNull())
                {
                    authenticator = this.GetComponentOrNull<EOSNetworkAuthenticator>();
                    if (authenticator.IsNull())
                    {
                        authenticator = FindFirstObjectByType<EOSNetworkAuthenticator>();
                        if (authenticator.IsNull())
                        {
                            authenticator = gameObject.AddComponent<EOSNetworkAuthenticator>();
                        }
                        else
                        {
                            // Found in scene
                        }
                    }
                    else
                    {
                        // Found on current GameObject
                    }
                }
                else
                {
                    // Authenticator pre-assigned
                }

                // Verify transport binding to NetworkManager
                if (networkManager.IsNotNull() && eosTransport.IsNotNull())
                {
                    if (Transport.active != eosTransport)
                    {
                        Transport.active = eosTransport;
                    }
                    else
                    {
                        // Already active transport
                    }

                    // Wire whitelist predicate to EosTransport only if active whitelist is enabled
                    if (useActiveWhitelist)
                    {
                        eosTransport.ConnectionFilter = (remotePuid) =>
                        {
                            if (remotePuid.IsNull())
                            {
                                return false;
                            }
                            else
                            {
                                remotePuid.ToString(out string puidStr);
                                return IsPeerAuthorized(puidStr);
                            }
                        };
                    }
                    else
                    {
                        eosTransport.ConnectionFilter = null;
                    }
                }
                else
                {
                    // Waiting for components to initialize
                }

                // Wire authenticator to NetworkManager
                if (networkManager.IsNotNull() && authenticator.IsNotNull())
                {
                    if (networkManager.authenticator != authenticator)
                    {
                        networkManager.authenticator = authenticator;
                    }
                    else
                    {
                        // Authenticator already wired
                    }
                }
                else
                {
                    // NetworkManager or Authenticator missing
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in ResolveReferences: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts hosting a Mirror server over Epic Online Services P2P.
        /// </summary>
        public bool StartEosHost()
        {
            try
            {
                ResolveReferences();

                if (!EOSSDKComponent.Initialized)
                {
                    string errorMsg = "Cannot start EOS Host: EOS SDK is not yet initialized or logged in.";
                    Debug.LogError($"[EOSNetworkManagerBridge] {errorMsg}");
                    OnEosError?.Invoke(errorMsg);
                    return false;
                }
                else
                {
                    if (networkManager.IsNull())
                    {
                        string errorMsg = "Cannot start EOS Host: GameNetworkManager reference is missing.";
                        Debug.LogError($"[EOSNetworkManagerBridge] {errorMsg}");
                        OnEosError?.Invoke(errorMsg);
                        return false;
                    }
                    else
                    {
                        if (NetworkServer.active || NetworkClient.active)
                        {
                            string errorMsg = "Network session is already active.";
                            Debug.LogWarning($"[EOSNetworkManagerBridge] {errorMsg}");
                            return false;
                        }
                        else
                        {
                            string localProductId = EOSSDKComponent.LocalUserProductIdString;
                            networkManager.networkAddress = localProductId;
                            networkManager.StartHost();

#if UNITY_EDITOR
                            Debug.Log($"[EOSNetworkManagerBridge] EOS Host started successfully! Local Product ID: {localProductId}");
#endif
                            OnEosHostStarted?.Invoke(localProductId);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Exception in StartEosHost: {ex.Message}";
                Debug.LogError($"[EOSNetworkManagerBridge] {errorMsg}");
                OnEosError?.Invoke(errorMsg);
                return false;
            }
        }

        /// <summary>
        /// Connects to a remote host via their EOS Product User ID.
        /// </summary>
        public bool StartEosClient(string hostProductUserId)
        {
            try
            {
                ResolveReferences();

                if (!EOSSDKComponent.Initialized)
                {
                    string errorMsg = "Cannot connect via EOS: EOS SDK is not yet initialized.";
                    Debug.LogError($"[EOSNetworkManagerBridge] {errorMsg}");
                    OnEosError?.Invoke(errorMsg);
                    return false;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(hostProductUserId))
                    {
                        string errorMsg = "Host Epic Product User ID cannot be empty.";
                        Debug.LogError($"[EOSNetworkManagerBridge] {errorMsg}");
                        OnEosError?.Invoke(errorMsg);
                        return false;
                    }
                    else
                    {
                        if (networkManager.IsNull())
                        {
                            string errorMsg = "Cannot start EOS Client: GameNetworkManager reference is missing.";
                            Debug.LogError($"[EOSNetworkManagerBridge] {errorMsg}");
                            OnEosError?.Invoke(errorMsg);
                            return false;
                        }
                        else
                        {
                            if (NetworkServer.active || NetworkClient.active)
                            {
                                string errorMsg = "Network session is already active.";
                                Debug.LogWarning($"[EOSNetworkManagerBridge] {errorMsg}");
                                return false;
                            }
                            else
                            {
                                string trimmedHostId = hostProductUserId.Trim();
                                if (trimmedHostId.Length != 32)
                                {
                                    string errorMsg = $"Cannot connect: Target Host ID '{trimmedHostId}' has invalid length ({trimmedHostId.Length} characters). EOS Product IDs must be exactly 32 hexadecimal characters. Please check for accidental extra or missing characters.";
                                    Debug.LogError($"[EOSNetworkManagerBridge] {errorMsg}");
                                    OnEosError?.Invoke(errorMsg);
                                    return false;
                                }
                                else
                                {
                                    networkManager.networkAddress = trimmedHostId;
                                    networkManager.StartClient();

#if UNITY_EDITOR
                                    Debug.Log($"[EOSNetworkManagerBridge] Connecting to EOS Host: {trimmedHostId}...");
#endif
                                    OnEosClientStarted?.Invoke(trimmedHostId);
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Exception in StartEosClient: {ex.Message}";
                Debug.LogError($"[EOSNetworkManagerBridge] {errorMsg}");
                OnEosError?.Invoke(errorMsg);
                return false;
            }
        }

        /// <summary>
        /// Gracefully stops the active Host or Client session.
        /// </summary>
        public void StopSession()
        {
            try
            {
                ResolveReferences();

                if (networkManager.IsNull())
                {
                    return;
                }
                else
                {
                    if (NetworkServer.active && NetworkClient.isConnected)
                    {
                        networkManager.StopHost();
                    }
                    else if (NetworkClient.isConnected || NetworkClient.active)
                    {
                        networkManager.StopClient();
                    }
                    else if (NetworkServer.active)
                    {
                        networkManager.StopServer();
                    }
                    else
                    {
                        // No active server or client session
                    }

#if UNITY_EDITOR
                    Debug.Log("[EOSNetworkManagerBridge] EOS Network session stopped.");
#endif
                    OnEosSessionStopped?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in StopSession: {ex.Message}");
            }
        }

        /// <summary>
        /// Copies the local EOS Product ID to system clipboard for easy peer sharing.
        /// </summary>
        public bool CopyHostAddressToClipboard()
        {
            try
            {
                string id = LocalProductId;
                if (!string.IsNullOrEmpty(id))
                {
                    GUIUtility.systemCopyBuffer = id;
                    return true;
                }
                else
                {
                    Debug.LogWarning("[EOSNetworkManagerBridge] Cannot copy Product ID: EOS is not initialized or ID is empty.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSNetworkManagerBridge] Exception in CopyHostAddressToClipboard: {ex.Message}");
                return false;
            }
        }
    }
}
