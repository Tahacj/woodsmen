using System;
using Mirror;
using UnityEngine;
using Woodsmen.Inventory;
using JustAGame.Pooling;

namespace JustAGame.Core.Network
{
    [AddComponentMenu("JustAGame/Game Network Manager")]
    public class GameNetworkManager : NetworkManager
    {
        [SerializeField] private Transform[] spawnPoints;

        private int _manualSpawnIndex = 0;

        // Cached host inventory for odd/even routing
        public static PlayerInventory HostInventory { get; private set; }
        public static event Action<PlayerInventory> OnHostInventoryAssigned;

        public static void SetHostInventory(PlayerInventory inventory)
        {
            HostInventory = inventory;
            OnHostInventoryAssigned?.Invoke(inventory);
        }

        // Platform lifecycle events for clients and servers
        public static event Action OnClientConnectedToServer;
        public static event Action OnClientDisconnectedFromServer;
        public static event Action<TransportError, string> OnClientTransportError;
        public static event Action<NetworkConnectionToClient, TransportError, string> OnServerTransportError;

        public override Transform GetStartPosition()
        {
            try
            {
                // Use manual spawn points if assigned; otherwise fallback to Mirror defaults
                if (spawnPoints.IsNotNull() && spawnPoints.Length > 0)
                {
                    var validPoints = Array.FindAll(spawnPoints, p => p.IsNotNull());
                    if (validPoints.Length > 0)
                    {
                        if (playerSpawnMethod == PlayerSpawnMethod.Random)
                        {
                            return validPoints[UnityEngine.Random.Range(0, validPoints.Length)];
                        }
                        else
                        {
                            Transform point = validPoints[_manualSpawnIndex % validPoints.Length];
                            _manualSpawnIndex = (_manualSpawnIndex + 1) % validPoints.Length;
                            return point;
                        }
                    }
                    else
                    {
                        return base.GetStartPosition();
                    }
                }
                else
                {
                    return base.GetStartPosition();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameNetworkManager] Exception in GetStartPosition: {ex.Message}");
                return base.GetStartPosition();
            }
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            try
            {
                base.OnServerAddPlayer(conn);

                // Register host inventory when local host player spawns
                if (conn == NetworkServer.localConnection && conn.identity.IsNotNull())
                {
                    var inventory = conn.identity.GetComponentOrNull<PlayerInventory>();
                    if (inventory.IsNotNull())
                    {
                        HostInventory = inventory;
                        OnHostInventoryAssigned?.Invoke(inventory);
                    }
                    else
                    {
                        Debug.LogWarning("[GameNetworkManager] PlayerInventory component not found on local host player.");
                    }
                }
                else
                {
                    // Remote player connected or host identity not spawned
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameNetworkManager] Exception in OnServerAddPlayer: {ex.Message}");
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            try
            {
                if (conn == NetworkServer.localConnection)
                {
                    HostInventory = null;
                }
                else
                {
                    // Non-host client disconnected
                }

                base.OnServerDisconnect(conn);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameNetworkManager] Exception in OnServerDisconnect: {ex.Message}");
                base.OnServerDisconnect(conn);
            }
        }

        public override void OnStopServer()
        {
            try
            {
                HostInventory = null;
                base.OnStopServer();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameNetworkManager] Exception in OnStopServer: {ex.Message}");
                base.OnStopServer();
            }
        }

        public override void OnClientConnect()
        {
            try
            {
                base.OnClientConnect();
                OnClientConnectedToServer?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameNetworkManager] Exception in OnClientConnect: {ex.Message}");
            }
        }

        public override void OnClientDisconnect()
        {
            try
            {
                base.OnClientDisconnect();
                OnClientDisconnectedFromServer?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameNetworkManager] Exception in OnClientDisconnect: {ex.Message}");
            }
        }

        public override void OnClientError(TransportError error, string reason)
        {
            try
            {
                base.OnClientError(error, reason);
                OnClientTransportError?.Invoke(error, reason);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameNetworkManager] Exception in OnClientError: {ex.Message}");
            }
        }

        public override void OnServerError(NetworkConnectionToClient conn, TransportError error, string reason)
        {
            try
            {
                base.OnServerError(conn, error, reason);
                OnServerTransportError?.Invoke(conn, error, reason);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameNetworkManager] Exception in OnServerError: {ex.Message}");
            }
        }

        // Resolves host inventory reference on the server
        public static PlayerInventory GetHostInventory()
        {
            try
            {
                if (HostInventory.IsNotNull())
                {
                    return HostInventory;
                }
                else
                {
                    if (NetworkServer.localConnection.IsNotNull() && NetworkServer.localConnection.identity.IsNotNull())
                    {
                        HostInventory = NetworkServer.localConnection.identity.GetComponentOrNull<PlayerInventory>();
                    }
                    else
                    {
                        // Local host connection not available
                    }

                    return HostInventory;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameNetworkManager] Exception in GetHostInventory: {ex.Message}");
                return null;
            }
        }
    }
}
