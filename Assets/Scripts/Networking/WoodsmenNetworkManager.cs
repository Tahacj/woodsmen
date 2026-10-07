using System;
using System.Collections.Generic;
using EpicTransport;
using JustAGame;
using JustAGame.Core.Network;
using JustAGame.Core.Platform;
using JustAGame.Pooling;
using Mirror;
using UnityEngine;
using Woodsmen.UI;

namespace Woodsmen.Networking
{
    /// <summary>
    /// Custom NetworkManager for Woodsmen, inheriting from GameNetworkManager for EOS Platform integration.
    /// Manages the Lobby -> Gameplay transition:
    /// 1. In Main Menu: Spawns WoodsmenLobbyPlayer for each connecting player.
    /// 2. Records each player's chosen CharacterClass (Lumberjack or Warrior).
    /// 3. On StartGame: Transitions scene to Gameplay Scene.
    /// 4. In Gameplay Scene: Spawns the appropriate character prefab (Lumberjack vs Warrior)
    ///    for each player connection.
    /// </summary>
    [DisallowMultipleComponent]
    public class WoodsmenNetworkManager : GameNetworkManager
    {
        public static new WoodsmenNetworkManager Instance => singleton as WoodsmenNetworkManager;

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
            try
            {
                maxConnections = 2; // Strict 2 players max in room (Host + 1 Client)
                if (transport.IsNull())
                {
                    transport = GetComponent<Transport>() ?? FindFirstObjectByType<EosTransport>();
                }
                else
                {
                    // Transport pre-assigned
                }

                EnsurePrefabsAssigned();
                base.Awake();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in Awake: {ex.Message}");
            }
        }

        public void EnsurePrefabsAssigned()
        {
            try
            {
                if (lobbyPlayerPrefab.IsNull())
                {
                    lobbyPlayerPrefab = Resources.Load<GameObject>("Network/WoodsmenLobbyPlayer");
#if UNITY_EDITOR
                    if (lobbyPlayerPrefab.IsNull())
                    {
                        lobbyPlayerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Network/WoodsmenLobbyPlayer.prefab");
                    }
                    else
                    {
                        // Loaded from resources
                    }
#endif
                }
                else
                {
                    // Already assigned
                }

                if (playerPrefab.IsNull() && lobbyPlayerPrefab.IsNotNull())
                {
                    playerPrefab = lobbyPlayerPrefab;
                }
                else
                {
                    // Player prefab configured
                }

                if (lumberjackPrefab.IsNull())
                {
#if UNITY_EDITOR
                    lumberjackPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Players/Lumberjack.prefab");
#endif
                    if (lumberjackPrefab.IsNull())
                    {
                        lumberjackPrefab = Resources.Load<GameObject>("Players/Lumberjack");
                    }
                    else
                    {
                        // Loaded from asset database
                    }
                }
                else
                {
                    // Already assigned
                }

                if (warriorPrefab.IsNull())
                {
#if UNITY_EDITOR
                    warriorPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Players/Warrior.prefab");
#endif
                    if (warriorPrefab.IsNull())
                    {
                        warriorPrefab = Resources.Load<GameObject>("Players/Warrior");
                    }
                    else
                    {
                        // Loaded from asset database
                    }
                }
                else
                {
                    // Already assigned
                }

                if (goblinPrefab.IsNull())
                {
#if UNITY_EDITOR
                    goblinPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Enemies/Goblin lvl1.prefab");
#endif
                    if (goblinPrefab.IsNull())
                    {
                        goblinPrefab = Resources.Load<GameObject>("Enemies/Goblin lvl1");
                    }
                    else
                    {
                        // Loaded from asset database
                    }
                }
                else
                {
                    // Already assigned
                }

                RegisterPrefabSafe(lobbyPlayerPrefab);
                RegisterPrefabSafe(lumberjackPrefab);
                RegisterPrefabSafe(warriorPrefab);
                RegisterPrefabSafe(goblinPrefab);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in EnsurePrefabsAssigned: {ex.Message}");
            }
        }

        private void RegisterPrefabSafe(GameObject prefab)
        {
            try
            {
                if (prefab.IsNotNull())
                {
                    if (spawnPrefabs.IsNotNull() && !spawnPrefabs.Contains(prefab))
                    {
                        spawnPrefabs.Add(prefab);
                    }
                    else
                    {
                        // Already in spawnPrefabs list
                    }

                    NetworkClient.RegisterPrefab(prefab);
                }
                else
                {
                    // Prefab is null
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{GetType().Name}] RegisterPrefabSafe notice for {(prefab != null ? prefab.name : "null")}: {ex.Message}");
            }
        }

        #region Lobby API

        public void HandleCreateRoom(string roomCode)
        {
            try
            {
                Debug.Log("[WoodsmenNetworkManager] Creating Room as Host...");
                _playerClassMap.Clear();

                StartHost();

                if (MainMenuManager.Instance.IsNotNull())
                {
                    MainMenuManager.Instance.ShowLobbyRoomWindow();
                }
                else
                {
                    // MainMenuManager not active
                }

                if (!string.IsNullOrEmpty(roomCode) && LobbyRoomUI.Instance.IsNotNull())
                {
                    LobbyRoomUI.Instance.SetRoomCode(roomCode);
                }
                else
                {
                    // Room code empty or LobbyRoomUI null
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in HandleCreateRoom: {ex.Message}");
            }
        }

        public void HandleJoinRoom(string roomCode)
        {
            try
            {
                Debug.Log($"[WoodsmenNetworkManager] Joining Room with code: {roomCode}...");

                networkAddress = "localhost";
                StartClient();

                if (MainMenuManager.Instance.IsNotNull())
                {
                    MainMenuManager.Instance.ShowLobbyRoomWindow();
                }
                else
                {
                    // MainMenuManager not active
                }

                if (LobbyRoomUI.Instance.IsNotNull())
                {
                    LobbyRoomUI.Instance.SetRoomCode(roomCode);
                }
                else
                {
                    // LobbyRoomUI null
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in HandleJoinRoom: {ex.Message}");
            }
        }

        public void StartGame()
        {
            try
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                else
                {
                    // Cache class choices from lobby players before scene load
                    foreach (var lobbyPlayer in WoodsmenLobbyPlayer.AllPlayers)
                    {
                        if (lobbyPlayer.IsNotNull() && lobbyPlayer.connectionToClient.IsNotNull())
                        {
                            _playerClassMap[lobbyPlayer.connectionToClient.connectionId] = lobbyPlayer.SelectedClass;
                            Debug.Log($"[WoodsmenNetworkManager] Cached class for conn {lobbyPlayer.connectionToClient.connectionId}: {lobbyPlayer.SelectedClass}");
                        }
                        else
                        {
                            // Lobby player or connection is null
                        }
                    }

                    Debug.Log($"[WoodsmenNetworkManager] Server changing scene to '{gameplayScene}'...");
                    ServerChangeScene(gameplayScene);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in StartGame: {ex.Message}");
            }
        }

        private bool _isLeavingRoom = false;

        public void LeaveRoom()
        {
            if (_isLeavingRoom)
            {
                return;
            }
            else
            {
                try
                {
                    _isLeavingRoom = true;
                    _playerClassMap.Clear();

                    var local = WoodsmenLobbyPlayer.LocalPlayer;
                    if (local.IsNotNull())
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
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[WoodsmenNetworkManager] Exception sending leave message: {ex.Message}");
                        }
                    }
                    else
                    {
                        // No local lobby player
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
                    else
                    {
                        // No active server/client
                    }

                    WoodsmenLobbyPlayer.ResetLobbyData();

                    if (GamePlatform.Network.IsNotNull())
                    {
                        GamePlatform.Network.StopSession();
                    }
                    else
                    {
                        // GamePlatform.Network not active
                    }

                    if (MainMenuManager.Instance.IsNotNull())
                    {
                        MainMenuManager.Instance.ReturnToMainMenu();
                    }
                    else
                    {
                        // MainMenuManager not active
                    }
                }
                finally
                {
                    _isLeavingRoom = false;
                }
            }
        }

        #endregion

        #region Server Lifecycle Overrides

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            try
            {
                base.OnServerConnect(conn);

                // Strict limit: reject connection if more than 2 connections (1 host + 1 client)
                if (NetworkServer.connections.Count > 2)
                {
                    Debug.LogWarning($"[WoodsmenNetworkManager] Rejecting connection {conn.connectionId}: Room is already full (max 2 players).");
                    conn.Disconnect();
                }
                else
                {
                    // Connection accepted within 2 player limit
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in OnServerConnect: {ex.Message}");
            }
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            try
            {
                if (conn.IsNull())
                {
                    return;
                }
                else
                {
                    // Prevent duplicate player addition if this connection already has an active identity
                    if (conn.identity.IsNotNull())
                    {
                        Debug.LogWarning($"[WoodsmenNetworkManager] Player already exists for connection {conn.connectionId}.");
                        return;
                    }
                    else
                    {
                        // Identity is null, proceeding to spawn
                    }
                }

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
                    else
                    {
                        // We are in Main Menu / Lobby: spawn the lobby player
                        if (lobbyPlayerPrefab.IsNotNull())
                        {
                            Transform startPos = GetStartPosition();
                            GameObject lobbyPlayerObj = startPos.IsNotNull()
                                ? Instantiate(lobbyPlayerPrefab, startPos.position, startPos.rotation)
                                : Instantiate(lobbyPlayerPrefab);
                            lobbyPlayerObj.name = $"LobbyPlayer [connId={conn.connectionId}]";
                            NetworkServer.AddPlayerForConnection(conn, lobbyPlayerObj);
                        }
                        else
                        {
                            Debug.LogWarning("[WoodsmenNetworkManager] Lobby player prefab is not assigned!");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in OnServerAddPlayer: {ex.Message}");
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            try
            {
                _playerClassMap.Remove(conn.connectionId);
                WoodsmenLobbyPlayer.RemovePlayerByConnection(conn);
                base.OnServerDisconnect(conn);
                WoodsmenLobbyPlayer.TriggerPlayersUpdated();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in OnServerDisconnect: {ex.Message}");
            }
        }

        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            try
            {
                base.OnServerReady(conn);

                if (IsSceneGameplay(networkSceneName))
                {
                    if (conn.IsNotNull())
                    {
                        if (conn.identity.IsNull() || conn.identity.GetComponent<WoodsmenLobbyPlayer>().IsNotNull())
                        {
                            SpawnGameplayCharacter(conn);
                        }
                        else
                        {
                            // Gameplay character already active for this connection
                        }
                    }
                    else
                    {
                        // Connection is null
                    }
                }
                else
                {
                    // Not gameplay scene
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in OnServerReady: {ex.Message}");
            }
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            try
            {
                base.OnServerSceneChanged(sceneName);

                if (IsSceneGameplay(sceneName))
                {
                    Debug.Log("[WoodsmenNetworkManager] Switched to Gameplay Scene! Spawning players with chosen classes...");
                    foreach (var conn in NetworkServer.connections.Values)
                    {
                        if (conn.IsNotNull() && conn.isReady)
                        {
                            if (conn.identity.IsNull() || conn.identity.GetComponent<WoodsmenLobbyPlayer>().IsNotNull())
                            {
                                SpawnGameplayCharacter(conn);
                            }
                            else
                            {
                                // Gameplay character already active for this connection
                            }
                        }
                        else
                        {
                            // Connection null or not ready
                        }
                    }
                }
                else
                {
                    // Not gameplay scene
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in OnServerSceneChanged: {ex.Message}");
            }
        }

        private void SpawnGameplayCharacter(NetworkConnectionToClient conn)
        {
            try
            {
                if (conn.IsNull())
                {
                    return;
                }
                else
                {
                    // Guard against double spawning
                    if (conn.identity.IsNotNull() && conn.identity.GetComponent<WoodsmenLobbyPlayer>().IsNull())
                    {
                        Debug.Log($"[WoodsmenNetworkManager] Connection {conn.connectionId} already has gameplay character: {conn.identity.name}");
                        return;
                    }
                    else
                    {
                        // Proceed to spawn gameplay character
                    }
                }

                CharacterClass chosenClass = CharacterClass.Lumberjack;
                if (_playerClassMap.TryGetValue(conn.connectionId, out var recordedClass))
                {
                    chosenClass = recordedClass;
                }
                else
                {
                    // Default to Lumberjack
                }

                GameObject prefabToSpawn = chosenClass == CharacterClass.Warrior ? warriorPrefab : lumberjackPrefab;
                if (prefabToSpawn.IsNull())
                {
                    Debug.LogError($"[WoodsmenNetworkManager] Missing prefab for class {chosenClass}!");
                    return;
                }
                else
                {
                    Transform startPos = GetClassStartPosition(chosenClass);
                    Vector3 pos = startPos.IsNotNull() ? startPos.position : Vector3.zero;
                    Quaternion rot = startPos.IsNotNull() ? startPos.rotation : Quaternion.identity;

                    GameObject characterInstance = Instantiate(prefabToSpawn, pos, rot);
                    characterInstance.name = $"{chosenClass}_{conn.connectionId}";

                    if (conn.identity.IsNotNull())
                    {
                        // Replace lobby player object with active gameplay character and destroy the lobby object
                        NetworkServer.ReplacePlayerForConnection(conn, characterInstance, ReplacePlayerOptions.Destroy);
                    }
                    else
                    {
                        NetworkServer.AddPlayerForConnection(conn, characterInstance);
                    }

                    if (conn == NetworkServer.localConnection)
                    {
                        var inventory = characterInstance.GetComponentOrNull<Woodsmen.Inventory.PlayerInventory>();
                        if (inventory.IsNotNull())
                        {
                            SetHostInventory(inventory);
                        }
                        else
                        {
                            // Character has no PlayerInventory component
                        }
                    }
                    else
                    {
                        // Remote client character
                    }

                    Debug.Log($"[WoodsmenNetworkManager] Spawned {chosenClass} for connection {conn.connectionId} at {pos}.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in SpawnGameplayCharacter: {ex.Message}");
            }
        }

        private Transform GetClassStartPosition(CharacterClass chosenClass)
        {
            try
            {
                var points = FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
                for (int i = 0; i < points.Length; i++)
                {
                    if (points[i].IsNotNull() && points[i].PreferredClass == chosenClass)
                    {
                        return points[i].transform;
                    }
                    else
                    {
                        continue;
                    }
                }

                return GetStartPosition();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in GetClassStartPosition: {ex.Message}");
                return GetStartPosition();
            }
        }

        public override void OnClientDisconnect()
        {
            try
            {
                base.OnClientDisconnect();
                Debug.Log("[WoodsmenNetworkManager] Client disconnected.");
                WoodsmenLobbyPlayer.ResetLobbyData();

                if (MainMenuManager.Instance.IsNotNull())
                {
                    MainMenuManager.Instance.ReturnToMainMenu();
                }
                else
                {
                    // Fallback: If disconnected while in Gameplay Scene,
                    // MainMenuManager does not exist, so manually load the Main Menu scene.
                    UnityEngine.SceneManagement.SceneManager.LoadScene("Main Menu");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Exception in OnClientDisconnect: {ex.Message}");
            }
        }

        private bool IsSceneGameplay(string sceneName)
        {
            return !string.IsNullOrEmpty(sceneName) && sceneName.Contains("Gameplay");
        }

        #endregion
    }
}
