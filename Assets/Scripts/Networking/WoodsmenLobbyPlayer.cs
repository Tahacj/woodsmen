using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Woodsmen.UI;

namespace Woodsmen.Networking
{
    /// <summary>
    /// Networked player representation while in the Lobby Room.
    /// Synchronizes player name, host status, ready status, and selected character class
    /// (Lumberjack or Warrior) across all connected clients.
    /// </summary>
    [DisallowMultipleComponent]
    public class WoodsmenLobbyPlayer : NetworkBehaviour
    {
        public static readonly List<WoodsmenLobbyPlayer> AllPlayers = new List<WoodsmenLobbyPlayer>();
        public static event Action OnLobbyPlayersUpdated;

        public static WoodsmenLobbyPlayer LocalPlayer { get; private set; }

        [SyncVar(hook = nameof(OnPlayerNameChanged))]
        private string playerName = "Player";

        [SyncVar(hook = nameof(OnHostStatusChanged))]
        private bool isHost;

        [SyncVar(hook = nameof(OnReadyStatusChanged))]
        private bool isReady;

        [SyncVar(hook = nameof(OnClassSelectionChanged))]
        private CharacterClass selectedClass = CharacterClass.Lumberjack;

        public string PlayerName => playerName;
        public bool IsHost => isHost;
        public bool IsReady => isReady;
        public CharacterClass SelectedClass => selectedClass;

        public static void SanitizePlayersList()
        {
            AllPlayers.RemoveAll(p => p == null);
        }

        public static void TriggerPlayersUpdated()
        {
            SanitizePlayersList();
            OnLobbyPlayersUpdated?.Invoke();
        }

        public static void ResetLobbyData()
        {
            AllPlayers.Clear();
            LocalPlayer = null;
            OnLobbyPlayersUpdated?.Invoke();
        }

        public static void RemovePlayer(WoodsmenLobbyPlayer player)
        {
            if (player != null && AllPlayers.Contains(player))
            {
                AllPlayers.Remove(player);
            }
            TriggerPlayersUpdated();
        }

        public static void RemovePlayerByConnection(NetworkConnectionToClient conn)
        {
            if (conn == null) return;
            for (int i = AllPlayers.Count - 1; i >= 0; i--)
            {
                var p = AllPlayers[i];
                if (p == null || p.connectionToClient == conn || (p.netIdentity != null && p.netIdentity.connectionToClient == conn))
                {
                    AllPlayers.RemoveAt(i);
                }
            }
            TriggerPlayersUpdated();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            SanitizePlayersList();
            if (!AllPlayers.Contains(this))
            {
                AllPlayers.Add(this);
            }

            // First player to connect is host
            if (connectionToClient != null && connectionToClient.connectionId == 0)
            {
                isHost = true;
                isReady = true;
                selectedClass = CharacterClass.Lumberjack;
            }
            else
            {
                selectedClass = GetAvailableClass();
            }

            int playerNumber = AllPlayers.Count;
            playerName = isHost ? $"Host (Player {playerNumber})" : $"Player {playerNumber}";

            TriggerPlayersUpdated();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();

            if (AllPlayers.Contains(this))
            {
                AllPlayers.Remove(this);
            }

            if (LocalPlayer == this)
            {
                LocalPlayer = null;
            }

            TriggerPlayersUpdated();
        }

        private CharacterClass GetAvailableClass()
        {
            bool lumberjackTaken = false;
            bool warriorTaken = false;

            foreach (var p in AllPlayers)
            {
                if (p == this || p == null) continue;
                if (p.SelectedClass == CharacterClass.Lumberjack) lumberjackTaken = true;
                if (p.SelectedClass == CharacterClass.Warrior) warriorTaken = true;
            }

            if (!lumberjackTaken) return CharacterClass.Lumberjack;
            if (!warriorTaken) return CharacterClass.Warrior;
            return CharacterClass.Lumberjack;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            SanitizePlayersList();
            if (!AllPlayers.Contains(this))
            {
                AllPlayers.Add(this);
            }

            if (isLocalPlayer)
            {
                LocalPlayer = this;
            }

            TriggerPlayersUpdated();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (AllPlayers.Contains(this))
            {
                AllPlayers.Remove(this);
            }

            if (LocalPlayer == this)
            {
                LocalPlayer = null;
            }

            TriggerPlayersUpdated();
        }

        private void OnDestroy()
        {
            if (AllPlayers.Contains(this))
            {
                AllPlayers.Remove(this);
            }

            if (LocalPlayer == this)
            {
                LocalPlayer = null;
            }

            TriggerPlayersUpdated();
        }

        #region SyncVar Hooks

        private void OnPlayerNameChanged(string oldName, string newName)
        {
            TriggerPlayersUpdated();
        }

        private void OnHostStatusChanged(bool oldVal, bool newVal)
        {
            TriggerPlayersUpdated();
        }

        private void OnReadyStatusChanged(bool oldVal, bool newVal)
        {
            TriggerPlayersUpdated();
        }

        private void OnClassSelectionChanged(CharacterClass oldClass, CharacterClass newClass)
        {
            TriggerPlayersUpdated();
        }

        #endregion

        #region Commands & RPCs

        [Command]
        public void CmdSetPlayerName(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            playerName = newName.Trim();
        }

        [Command]
        public void CmdSelectClass(CharacterClass newClass)
        {
            // Only allow Lumberjack or Warrior for play
            if (newClass != CharacterClass.Lumberjack && newClass != CharacterClass.Warrior) return;

            // Enforce uniqueness: check if another player already claimed this class
            foreach (var p in AllPlayers)
            {
                if (p != this && p != null && p.SelectedClass == newClass)
                {
                    Debug.LogWarning($"[WoodsmenLobbyPlayer] Cannot select {newClass}: already taken by {p.PlayerName}");
                    TargetClassAlreadyTaken(newClass);
                    return;
                }
            }

            selectedClass = newClass;
            Debug.Log($"[WoodsmenLobbyPlayer] Player {playerName} selected class: {newClass}");
        }

        [TargetRpc]
        private void TargetClassAlreadyTaken(CharacterClass takenClass)
        {
            LobbyRoomUI.Instance?.ShowClassUnavailableFeedback(takenClass);
        }

        [Command]
        public void CmdSetReady(bool ready)
        {
            isReady = ready;
        }

        /// <summary>
        /// Sent from a client to the server when leaving the lobby room.
        /// Immediately removes the leaving player, informs other players, and destroys the object.
        /// </summary>
        [Command]
        public void CmdLeaveLobby()
        {
            Debug.Log($"[WoodsmenLobbyPlayer] Server received CmdLeaveLobby for {playerName} (conn {connectionToClient?.connectionId}).");

            if (AllPlayers.Contains(this))
            {
                AllPlayers.Remove(this);
            }

            TriggerPlayersUpdated();

            if (gameObject != null)
            {
                NetworkServer.Destroy(gameObject);
            }
        }

        /// <summary>
        /// Sent from the host to all clients when the host closes the room.
        /// </summary>
        [ClientRpc]
        public void RpcHostLeavingLobby()
        {
            Debug.Log("[WoodsmenLobbyPlayer] Received RpcHostLeavingLobby: Host is closing the room.");
            if (isServer) return; // Host is already tearing down

            if (WoodsmenNetworkManager.Instance != null)
            {
                WoodsmenNetworkManager.Instance.LeaveRoom();
            }
            else if (MainMenuManager.Instance != null)
            {
                MainMenuManager.Instance.ReturnToMainMenu();
            }
        }

        #endregion
    }
}
