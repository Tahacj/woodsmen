using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Woodsmen.UI;

namespace Woodsmen.Networking
{
    /// <summary>
    /// Custom NetworkManager for Woodsmen.
    /// Manages the Lobby -> Gameplay transition:
    /// 1. In Main Menu: Spawns WoodsmenLobbyPlayer for each connecting player.
    /// 2. Records each player's chosen CharacterClass (Lumberjack or Warrior).
    /// 3. On StartGame: Transitions scene to Gameplay Scene.
    /// 4. In Gameplay Scene: Spawns the appropriate character prefab (Lumberjack vs Warrior)
    ///    for each player connection.
    /// </summary>
    [DisallowMultipleComponent]
    public class WoodsmenNetworkManager : NetworkManager
    {
        public static WoodsmenNetworkManager Instance => singleton as WoodsmenNetworkManager;

        [Header("Woodsmen Character Prefabs")]
        [Tooltip("Prefab instantiated in lobby to sync player slots and class picks.")]
        [SerializeField] private GameObject lobbyPlayerPrefab;

        [Tooltip("Prefab spawned when player chooses Lumberjack.")]
        [SerializeField] private GameObject lumberjackPrefab;

        [Tooltip("Prefab spawned when player chooses Warrior.")]
        [SerializeField] private GameObject warriorPrefab;

        [Header("Enemy Prefabs")]
        [Tooltip("Prefab spawned when enemies are dynamically spawned.")]
        [SerializeField] private GameObject goblinPrefab;

        [Header("Scene Configuration")]
        [SerializeField] private string gameplayScene = "Gameplay Scene";

        // Stores player class choices mapped by connectionId
        private readonly Dictionary<int, CharacterClass> _playerClassMap = new Dictionary<int, CharacterClass>();

        public override void Awake()
        {
            maxConnections = 2; // Strict 2 players max in room (Host + 1 Client)
            if (transport == null)
            {
                transport = GetComponent<Transport>() ?? gameObject.AddComponent<UnityRelayTransport>();
            }
            EnsurePrefabsAssigned();
            base.Awake();
        }

        public void EnsurePrefabsAssigned()
        {
            if (lobbyPlayerPrefab == null)
            {
                lobbyPlayerPrefab = Resources.Load<GameObject>("Network/WoodsmenLobbyPlayer");
#if UNITY_EDITOR
                if (lobbyPlayerPrefab == null)
                {
                    lobbyPlayerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Network/WoodsmenLobbyPlayer.prefab");
                }
#endif
            }

            if (playerPrefab == null && lobbyPlayerPrefab != null)
            {
                playerPrefab = lobbyPlayerPrefab;
            }

            if (lumberjackPrefab == null)
            {
#if UNITY_EDITOR
                lumberjackPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Players/Lumberjack.prefab");
#endif
                if (lumberjackPrefab == null)
                {
                    lumberjackPrefab = Resources.Load<GameObject>("Players/Lumberjack");
                }
            }

            if (warriorPrefab == null)
            {
#if UNITY_EDITOR
                warriorPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Players/Warrior.prefab");
#endif
                if (warriorPrefab == null)
                {
                    warriorPrefab = Resources.Load<GameObject>("Players/Warrior");
                }
            }

            if (goblinPrefab == null)
            {
#if UNITY_EDITOR
                goblinPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Enemies/Goblin lvl1.prefab");
#endif
                if (goblinPrefab == null)
                {
                    goblinPrefab = Resources.Load<GameObject>("Enemies/Goblin lvl1");
                }
            }

            RegisterSpawnablePrefabs();
        }

        public override void Start()
        {
            base.Start();

            // Link MainMenuManager events if present
            if (MainMenuManager.Instance != null)
            {
                MainMenuManager.Instance.OnLeaveRoomClicked += LeaveRoom;
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (MainMenuManager.Instance != null)
            {
                MainMenuManager.Instance.OnLeaveRoomClicked -= LeaveRoom;
            }
        }

        private void RegisterSpawnablePrefabs()
        {
            if (lobbyPlayerPrefab != null && !spawnPrefabs.Contains(lobbyPlayerPrefab))
                spawnPrefabs.Add(lobbyPlayerPrefab);

            if (lumberjackPrefab != null && !spawnPrefabs.Contains(lumberjackPrefab))
                spawnPrefabs.Add(lumberjackPrefab);

            if (warriorPrefab != null && !spawnPrefabs.Contains(warriorPrefab))
                spawnPrefabs.Add(warriorPrefab);

            if (goblinPrefab != null && !spawnPrefabs.Contains(goblinPrefab))
                spawnPrefabs.Add(goblinPrefab);
        }

        #region Room / Lobby Actions

        public void HandleCreateRoom()
        {
            HandleCreateRoom(null);
        }

        public void HandleCreateRoom(string roomCode)
        {
            Debug.Log("[WoodsmenNetworkManager] Creating Room as Host...");
            _playerClassMap.Clear();

            // Start Host
            StartHost();

            // Show Lobby UI
            if (MainMenuManager.Instance != null)
            {
                MainMenuManager.Instance.ShowLobbyRoomWindow();
            }

            if (!string.IsNullOrEmpty(roomCode) && LobbyRoomUI.Instance != null)
            {
                LobbyRoomUI.Instance.SetRoomCode(roomCode);
            }
        }

        public void HandleJoinRoom(string roomCode)
        {
            Debug.Log($"[WoodsmenNetworkManager] Joining Room with code: {roomCode}...");

            // If using Unity Relay, RelayManager configures transport with join code before StartClient()
            networkAddress = "localhost";
            StartClient();

            if (MainMenuManager.Instance != null)
            {
                MainMenuManager.Instance.ShowLobbyRoomWindow();
            }

            if (LobbyRoomUI.Instance != null)
            {
                LobbyRoomUI.Instance.SetRoomCode(roomCode);
            }
        }

        public void StartGame()
        {
            if (!NetworkServer.active) return;

            // Cache class choices from lobby players before scene load
            foreach (var lobbyPlayer in WoodsmenLobbyPlayer.AllPlayers)
            {
                if (lobbyPlayer.connectionToClient != null)
                {
                    _playerClassMap[lobbyPlayer.connectionToClient.connectionId] = lobbyPlayer.SelectedClass;
                    Debug.Log($"[WoodsmenNetworkManager] Cached class for conn {lobbyPlayer.connectionToClient.connectionId}: {lobbyPlayer.SelectedClass}");
                }
            }

            Debug.Log($"[WoodsmenNetworkManager] Server changing scene to '{gameplayScene}'...");
            ServerChangeScene(gameplayScene);
        }

        private bool _isLeavingRoom = false;

        public void LeaveRoom()
        {
            if (_isLeavingRoom) return;
            try
            {
                _isLeavingRoom = true;
                _playerClassMap.Clear();

                var local = WoodsmenLobbyPlayer.LocalPlayer;
                if (local != null)
                {
                    try
                    {
                        if (local.IsHost)
                        {
                            local.RpcHostLeavingLobby();
                        }
                        else
                        {
                            local.CmdLeaveLobby();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[WoodsmenNetworkManager] Exception sending leave message: {ex.Message}");
                    }
                }

                if (NetworkServer.active && NetworkClient.isConnected)
                {
                    StopHost();
                }
                else if (NetworkClient.isConnected)
                {
                    StopClient();
                }
                else if (NetworkServer.active)
                {
                    StopServer();
                }

                WoodsmenLobbyPlayer.ResetLobbyData();

                if (WoodsmenRelayManager.Instance != null)
                {
                    WoodsmenRelayManager.Instance.LeaveRoom();
                }

                if (MainMenuManager.Instance != null)
                {
                    MainMenuManager.Instance.ReturnToMainMenu();
                }
            }
            finally
            {
                _isLeavingRoom = false;
            }
        }

        #endregion

        #region Server Lifecycle Overrides

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);

            // Strict limit: reject connection if more than 2 connections (1 host + 1 client)
            if (NetworkServer.connections.Count > 2)
            {
                Debug.LogWarning($"[WoodsmenNetworkManager] Rejecting connection {conn.connectionId}: Room is already full (max 2 players).");
                conn.Disconnect();
            }
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            // If we are in the gameplay scene, spawn the chosen character
            if (IsSceneGameplay(networkSceneName))
            {
                SpawnGameplayCharacter(conn);
            }
            else
            {
                // Enforce max 2 players in lobby
                WoodsmenLobbyPlayer.SanitizePlayersList();
                if (WoodsmenLobbyPlayer.AllPlayers.Count >= 2)
                {
                    Debug.LogWarning($"[WoodsmenNetworkManager] Rejecting player add for connection {conn.connectionId}: Lobby already has 2 players!");
                    conn.Disconnect();
                    return;
                }

                // We are in Main Menu / Lobby: spawn the lobby player
                if (lobbyPlayerPrefab != null)
                {
                    GameObject lobbyPlayerObj = Instantiate(lobbyPlayerPrefab);
                    NetworkServer.AddPlayerForConnection(conn, lobbyPlayerObj);
                }
                else
                {
                    Debug.LogWarning("[WoodsmenNetworkManager] Lobby player prefab is not assigned!");
                }
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            _playerClassMap.Remove(conn.connectionId);
            WoodsmenLobbyPlayer.RemovePlayerByConnection(conn);
            base.OnServerDisconnect(conn);
            WoodsmenLobbyPlayer.TriggerPlayersUpdated();
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);

            if (IsSceneGameplay(sceneName))
            {
                Debug.Log("[WoodsmenNetworkManager] Switched to Gameplay Scene! Spawning players with chosen classes...");
                foreach (var conn in NetworkServer.connections.Values)
                {
                    if (conn != null && conn.isReady)
                    {
                        SpawnGameplayCharacter(conn);
                    }
                }
            }
        }

        private void SpawnGameplayCharacter(NetworkConnectionToClient conn)
        {
            // Determine chosen class (defaults to Lumberjack if unassigned)
            CharacterClass chosenClass = CharacterClass.Lumberjack;
            if (_playerClassMap.TryGetValue(conn.connectionId, out var recordedClass))
            {
                chosenClass = recordedClass;
            }

            GameObject prefabToSpawn = chosenClass == CharacterClass.Warrior ? warriorPrefab : lumberjackPrefab;
            if (prefabToSpawn == null)
            {
                Debug.LogError($"[WoodsmenNetworkManager] Missing prefab for class {chosenClass}!");
                return;
            }

            Transform startPos = GetClassStartPosition(chosenClass);
            Vector3 pos = startPos != null ? startPos.position : Vector3.zero;
            Quaternion rot = startPos != null ? startPos.rotation : Quaternion.identity;

            GameObject characterInstance = Instantiate(prefabToSpawn, pos, rot);
            characterInstance.name = $"{chosenClass}_{conn.connectionId}";

            // Replace lobby player object with active gameplay character
            NetworkServer.ReplacePlayerForConnection(conn, characterInstance);
            Debug.Log($"[WoodsmenNetworkManager] Spawned {chosenClass} for connection {conn.connectionId} at {pos}.");
        }

        private Transform GetClassStartPosition(CharacterClass chosenClass)
        {
            var spawnPoints = FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i].PreferredClass == chosenClass)
                {
                    return spawnPoints[i].transform;
                }
            }

            // Fallback to standard Mirror start position
            return GetStartPosition();
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            Debug.Log("[WoodsmenNetworkManager] Client disconnected.");
            WoodsmenLobbyPlayer.ResetLobbyData();
            
            if (MainMenuManager.Instance != null)
            {
                MainMenuManager.Instance.ReturnToMainMenu();
            }
            else
            {
                // Fallback: If the host Alt+F4s while in the Gameplay Scene, 
                // the MainMenuManager no longer exists, so we must manually load the menu.
                UnityEngine.SceneManagement.SceneManager.LoadScene("Main Menu");
            }
        }

        private bool IsSceneGameplay(string sceneName)
        {
            return !string.IsNullOrEmpty(sceneName) && sceneName.Contains("Gameplay");
        }

        #endregion
    }
}
