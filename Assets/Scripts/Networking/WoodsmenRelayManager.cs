using System;
using System.Threading.Tasks;
using Mirror;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Woodsmen.Networking
{
    /// <summary>
    /// Manages Unity Gaming Services (UGS) Authentication and Unity Relay allocations.
    /// Bridges Relay allocations with UnityRelayTransport for Mirror.
    /// Provides seamless fallback to local UDP if services are unreachable.
    /// </summary>
    [DisallowMultipleComponent]
    public class WoodsmenRelayManager : MonoBehaviour
    {
        public static WoodsmenRelayManager Instance { get; private set; }

        [Header("Relay Settings")]
        [SerializeField] private int maxPlayers = 2; // Max 2 players in room (Host + 1 Client)
        [SerializeField] private string connectionType = "udp"; // "udp" or "dtls"

        public string ConnectionType { get => connectionType; set => connectionType = value; }
        public string CurrentJoinCode { get; private set; } = string.Empty;
        public bool IsRelayActive { get; private set; }

        private bool _isAllocatingRelay = false;
        private bool _isJoiningRelay = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Pre-initialize UGS in background so lobby creation is instantaneous
            _ = EnsureUGSInitializedAsync();
        }

        /// <summary>
        /// Ensures Unity Gaming Services are initialized and anonymous auth is signed in.
        /// Returns (success, error message).
        /// </summary>
        public async Task<(bool success, string error)> EnsureUGSInitializedAsync()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    Debug.Log("[WoodsmenRelayManager] Initializing Unity Gaming Services...");
                    await UnityServices.InitializeAsync();
                    Debug.Log($"[WoodsmenRelayManager] Unity Services initialized. State: {UnityServices.State}");
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    Debug.Log("[WoodsmenRelayManager] Signing in anonymously...");
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log($"[WoodsmenRelayManager] Signed in anonymously. Player ID: {AuthenticationService.Instance.PlayerId}");
                }

                return (true, null);
            }
            catch (AuthenticationException aEx)
            {
                Debug.LogError($"[WoodsmenRelayManager] AuthenticationException: {aEx.ErrorCode} - {aEx.Message}");
                return (false, $"Auth failed: {aEx.Message}");
            }
            catch (RequestFailedException rfEx)
            {
                Debug.LogError($"[WoodsmenRelayManager] RequestFailedException: {rfEx.ErrorCode} - {rfEx.Message}");
                return (false, $"Network error: {rfEx.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WoodsmenRelayManager] UGS initialization / sign-in error: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Creates a room on Unity Relay server and starts Mirror Host.
        /// Returns (code, error message).
        /// </summary>
        public async Task<(string code, string error)> CreateRelayRoomAsync()
        {
            if (_isAllocatingRelay)
            {
                Debug.LogWarning("[WoodsmenRelayManager] Relay room allocation already in progress! Ignoring duplicate call.");
                return (CurrentJoinCode, "Allocation already in progress");
            }

            if (IsRelayActive && !string.IsNullOrEmpty(CurrentJoinCode))
            {
                Debug.LogWarning($"[WoodsmenRelayManager] Relay room already active with code: {CurrentJoinCode}. Returning existing code.");
                return (CurrentJoinCode, null);
            }

            _isAllocatingRelay = true;
            try
            {
                Debug.Log("[WoodsmenRelayManager] Creating Relay Room on Unity Cloud...");

                var ugs = await EnsureUGSInitializedAsync();
                if (!ugs.success)
                {
                    Debug.LogError($"[WoodsmenRelayManager] Cannot create room: {ugs.error}");
                    return (null, ugs.error);
                }

                // 1. Allocate relay server on Unity Cloud (maxConnections is client count excluding host)
                int clientSlots = Mathf.Max(1, maxPlayers - 1);
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(clientSlots);

                if (allocation.ServerEndpoints != null)
                {
                    foreach (var ep in allocation.ServerEndpoints)
                    {
                        Debug.Log($"[WoodsmenRelayManager] Host allocation endpoint: type={ep.ConnectionType}, host={ep.Host}, port={ep.Port}, secure={ep.Secure}");
                    }
                }

                // 2. Fetch the join code
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                CurrentJoinCode = joinCode;
                IsRelayActive = true;

                // 3. Convert allocation into Transport RelayServerData
                string protocol = ResolveBestConnectionType(allocation.ServerEndpoints);
                Debug.Log($"[WoodsmenRelayManager] Host using Relay protocol: {protocol}");
                RelayServerData serverData = allocation.ToRelayServerData(protocol);

                // 4. Configure transport and start host
                var transport = ResolveTransport();
                if (transport != null)
                {
                    transport.SetServerRelayData(serverData);
                }

                if (WoodsmenNetworkManager.Instance != null)
                {
                    WoodsmenNetworkManager.Instance.StartHost();
                }

                // 5. Verify host driver establishes connection to Relay server
                if (transport != null)
                {
                    float hostElapsed = 0f;
                    while (transport.ServerRelayStatus != Unity.Networking.Transport.Relay.RelayConnectionStatus.Established && hostElapsed < 3.0f)
                    {
                        await Task.Delay(50);
                        hostElapsed += 0.05f;
                    }
                    Debug.Log($"[WoodsmenRelayManager] Host Relay status after {hostElapsed:F2}s: {transport.ServerRelayStatus}");
                    if (transport.ServerRelayStatus == Unity.Networking.Transport.Relay.RelayConnectionStatus.AllocationInvalid)
                    {
                        LeaveRoom();
                        return (null, "Relay allocation became invalid. Please try again.");
                    }
                }

                Debug.Log($"[WoodsmenRelayManager] Unity Relay Room created! Join Code: {joinCode}");
                return (joinCode, null);
            }
            catch (RelayServiceException rEx)
            {
                Debug.LogError($"[WoodsmenRelayManager] RelayServiceException creating room: {rEx.Reason} - {rEx.Message}");
                LeaveRoom();
                return (null, $"Relay error: {rEx.Reason}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WoodsmenRelayManager] Unity Relay allocation failed: {ex}");
                LeaveRoom();
                return (null, ex.Message);
            }
            finally
            {
                _isAllocatingRelay = false;
            }
        }

        /// <summary>
        /// Joins an existing room on Unity Relay server and starts Mirror Client.
        /// Returns (success, error message).
        /// </summary>
        public async Task<(bool success, string error)> JoinRelayRoomAsync(string joinCode)
        {
            if (_isJoiningRelay)
            {
                Debug.LogWarning("[WoodsmenRelayManager] Joining Relay room already in progress! Ignoring duplicate call.");
                return (false, "Join already in progress");
            }

            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return (false, "Please enter an invitation code.");
            }

            joinCode = joinCode.Trim().ToUpperInvariant();

            // Local testing override keywords
            if (joinCode == "LOCAL" || joinCode == "LOCALHOST" || joinCode == "127.0.0.1")
            {
                Debug.Log("[WoodsmenRelayManager] Localhost override requested.");
                StartFallbackLocalClient();
                return (true, null);
            }

            // Relay invitation codes are typically 6 alphanumeric characters
            if (joinCode.Length < 6)
            {
                return (false, "Invalid code length. Relay invitation codes are 6 characters.");
            }

            var ugs = await EnsureUGSInitializedAsync();
            if (!ugs.success)
            {
                return (false, ugs.error);
            }

            _isJoiningRelay = true;
            try
            {
                Debug.Log($"[WoodsmenRelayManager] Joining Relay allocation with code: {joinCode}...");
                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                CurrentJoinCode = joinCode;
                IsRelayActive = true;

                if (joinAllocation.ServerEndpoints != null)
                {
                    foreach (var ep in joinAllocation.ServerEndpoints)
                    {
                        Debug.Log($"[WoodsmenRelayManager] Client join endpoint: type={ep.ConnectionType}, host={ep.Host}, port={ep.Port}, secure={ep.Secure}");
                    }
                }

                // 2. Convert to Transport RelayServerData
                string protocol = ResolveBestConnectionType(joinAllocation.ServerEndpoints);
                Debug.Log($"[WoodsmenRelayManager] Client using Relay protocol: {protocol}");
                RelayServerData clientData = joinAllocation.ToRelayServerData(protocol);

                // 3. Configure transport and start client
                var transport = ResolveTransport();
                if (transport != null)
                {
                    transport.SetClientRelayData(clientData);
                }

                if (WoodsmenNetworkManager.Instance != null)
                {
                    WoodsmenNetworkManager.Instance.networkAddress = "relay";
                    WoodsmenNetworkManager.Instance.StartClient();
                }

                // 4. Wait up to 8 seconds for Mirror connection handshake to confirm
                float timeout = 8f;
                float elapsed = 0f;
                float lastLogTime = 0f;
                while (!NetworkClient.isConnected && elapsed < timeout)
                {
                    await Task.Delay(50);
                    elapsed += 0.05f;

                    if (elapsed - lastLogTime >= 1.0f)
                    {
                        lastLogTime = elapsed;
                        var status = transport != null ? transport.ClientRelayStatus : Unity.Networking.Transport.Relay.RelayConnectionStatus.NotUsingRelay;
                        Debug.Log($"[WoodsmenRelayManager] Waiting for connection... Elapsed: {elapsed:F1}s / {timeout}s, ClientRelayStatus: {status}");
                        if (status == Unity.Networking.Transport.Relay.RelayConnectionStatus.AllocationInvalid)
                        {
                            Debug.LogError("[WoodsmenRelayManager] Client Relay allocation became AllocationInvalid!");
                            break;
                        }
                    }
                }

                if (!NetworkClient.isConnected)
                {
                    var finalStatus = transport != null ? transport.ClientRelayStatus : Unity.Networking.Transport.Relay.RelayConnectionStatus.NotUsingRelay;
                    Debug.LogWarning($"[WoodsmenRelayManager] Mirror client connection timed out after {timeout}s for code '{joinCode}'. Final RelayStatus: {finalStatus}");
                    if (WoodsmenNetworkManager.Instance != null)
                    {
                        WoodsmenNetworkManager.Instance.StopClient();
                    }
                    LeaveRoom();
                    return (false, "Connection timed out. Host may have closed the room.");
                }

                Debug.Log($"[WoodsmenRelayManager] Successfully connected to Relay room: {joinCode}");
                return (true, null);
            }
            catch (RelayServiceException rEx)
            {
                Debug.LogWarning($"[WoodsmenRelayManager] RelayServiceException joining '{joinCode}': {rEx.Reason} - {rEx.Message}");
                LeaveRoom();
                string errorMsg = "Room not found or code expired.";
                if (rEx.Message.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rEx.Reason.ToString().IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rEx.Reason.ToString().IndexOf("capacity", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    errorMsg = "Room is full (max 2 players).";
                }
                return (false, errorMsg);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WoodsmenRelayManager] Failed to join Relay room '{joinCode}': {ex.Message}");
                LeaveRoom();
                return (false, "Failed to join room. Please check the code and try again.");
            }
            finally
            {
                _isJoiningRelay = false;
            }
        }

        /// <summary>
        /// Leaves the current room and cleans up transport data.
        /// </summary>
        public void LeaveRoom()
        {
            _isAllocatingRelay = false;
            _isJoiningRelay = false;
            CurrentJoinCode = string.Empty;
            IsRelayActive = false;

            var transport = ResolveTransport();
            if (transport != null)
            {
                transport.ClearRelayData();
            }
        }

        private string StartFallbackLocalHost()
        {
            string localCode = UnityEngine.Random.Range(1000, 10000).ToString();
            CurrentJoinCode = localCode;
            IsRelayActive = false;

            var transport = ResolveTransport();
            if (transport != null)
            {
                transport.ClearRelayData();
            }

            if (WoodsmenNetworkManager.Instance != null)
            {
                WoodsmenNetworkManager.Instance.StartHost();
            }

            Debug.Log($"[WoodsmenRelayManager] Started local fallback host with code: {localCode}");
            return localCode;
        }

        private void StartFallbackLocalClient()
        {
            IsRelayActive = false;

            var transport = ResolveTransport();
            if (transport != null)
            {
                transport.ClearRelayData();
            }

            if (WoodsmenNetworkManager.Instance != null)
            {
                WoodsmenNetworkManager.Instance.networkAddress = "localhost";
                WoodsmenNetworkManager.Instance.StartClient();
            }

            Debug.Log("[WoodsmenRelayManager] Started local fallback client connecting to localhost.");
        }

        private string ResolveBestConnectionType(System.Collections.Generic.List<RelayServerEndpoint> endpoints)
        {
            if (endpoints == null || endpoints.Count == 0) return connectionType;

            // Check if the configured connectionType (e.g. "udp" or "dtls") is supported in this allocation
            foreach (var ep in endpoints)
            {
                if (string.Equals(ep.ConnectionType, connectionType, StringComparison.OrdinalIgnoreCase))
                    return connectionType;
            }

            // Otherwise, prefer "udp" if available, then "dtls"
            foreach (var ep in endpoints)
            {
                if (string.Equals(ep.ConnectionType, "udp", StringComparison.OrdinalIgnoreCase))
                    return "udp";
            }
            foreach (var ep in endpoints)
            {
                if (string.Equals(ep.ConnectionType, "dtls", StringComparison.OrdinalIgnoreCase))
                    return "dtls";
            }

            return endpoints[0].ConnectionType;
        }

        private UnityRelayTransport ResolveTransport()
        {
            if (UnityRelayTransport.Instance != null) return UnityRelayTransport.Instance;
            if (Transport.active is UnityRelayTransport urt) return urt;
            return FindFirstObjectByType<UnityRelayTransport>();
        }
    }
}
