using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Woodsmen.Networking;

namespace Woodsmen.UI
{
    /// <summary>
    /// Manages the in-room Lobby window.
    /// Handles:
    /// - Live refreshing connected player list
    /// - Character class selection (Lumberjack vs Warrior)
    /// - Invitation code display & 1-click clipboard copy
    /// - Start Match (for host) and Leave Room actions
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyRoomUI : MonoBehaviour
    {
        public static LobbyRoomUI Instance { get; private set; }

        [Header("Room Header")]
        [SerializeField] private TMP_Text roomCodeText;
        [SerializeField] private Button copyCodeButton;
        [SerializeField] private TMP_Text copyFeedbackText;
        [SerializeField] private TMP_Text playerCountText;

        [Header("Players List")]
        [SerializeField] private Transform playerListContainer;
        [SerializeField] private GameObject playerSlotPrefab;

        [Header("Class Selection")]
        [SerializeField] private Button selectLumberjackButton;
        [SerializeField] private Button selectWarriorButton;
        [SerializeField] private Image lumberjackHighlight;
        [SerializeField] private Image warriorHighlight;
        [SerializeField] private TMP_Text selectedClassStatusText;

        [Header("Room Actions")]
        [SerializeField] private Button startMatchButton;
        [SerializeField] private TMP_Text startMatchButtonText;
        [SerializeField] private Button leaveRoomButton;

        private readonly List<LobbyPlayerSlotUI> _activeSlots = new List<LobbyPlayerSlotUI>();
        private string _currentRoomCode = "----";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            SetupListeners();
        }

        private void OnEnable()
        {
            WoodsmenLobbyPlayer.OnLobbyPlayersUpdated += RefreshPlayerList;
            RefreshPlayerList();
            
            // Backup confirmation layer: auto-refresh to catch delayed network spawns
            InvokeRepeating(nameof(RefreshPlayerList), 1.5f, 1.5f);

            if (copyFeedbackText != null) copyFeedbackText.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            WoodsmenLobbyPlayer.OnLobbyPlayersUpdated -= RefreshPlayerList;
            CancelInvoke(nameof(RefreshPlayerList));
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RemoveListeners();
        }

        private void SetupListeners()
        {
            if (copyCodeButton != null)
                copyCodeButton.onClick.AddListener(CopyRoomCodeToClipboard);

            if (selectLumberjackButton != null)
                selectLumberjackButton.onClick.AddListener(() => OnClassButtonClicked(CharacterClass.Lumberjack));

            if (selectWarriorButton != null)
                selectWarriorButton.onClick.AddListener(() => OnClassButtonClicked(CharacterClass.Warrior));

            if (startMatchButton != null)
                startMatchButton.onClick.AddListener(OnStartMatchClicked);

            if (leaveRoomButton != null)
                leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
        }

        private void RemoveListeners()
        {
            if (copyCodeButton != null) copyCodeButton.onClick.RemoveListener(CopyRoomCodeToClipboard);
            if (selectLumberjackButton != null) selectLumberjackButton.onClick.RemoveAllListeners();
            if (selectWarriorButton != null) selectWarriorButton.onClick.RemoveAllListeners();
            if (startMatchButton != null) startMatchButton.onClick.RemoveListener(OnStartMatchClicked);
            if (leaveRoomButton != null) leaveRoomButton.onClick.RemoveListener(OnLeaveRoomClicked);
        }

        /// <summary>
        /// Updates the displayed invitation code (e.g. from Unity Relay).
        /// </summary>
        public void SetRoomCode(string code)
        {
            _currentRoomCode = code;
            if (roomCodeText != null)
            {
                roomCodeText.text = $"Room Code: <color=#FFD700><b>{code}</b></color>";
            }
        }

        private void CopyRoomCodeToClipboard()
        {
            if (string.IsNullOrEmpty(_currentRoomCode)) return;

            GUIUtility.systemCopyBuffer = _currentRoomCode;
            Debug.Log($"[LobbyRoomUI] Copied room code to clipboard: {_currentRoomCode}");

            if (copyFeedbackText != null)
            {
                copyFeedbackText.gameObject.SetActive(true);
                copyFeedbackText.text = "Copied!";
                CancelInvoke(nameof(HideCopyFeedback));
                Invoke(nameof(HideCopyFeedback), 2f);
            }
        }

        private void HideCopyFeedback()
        {
            if (copyFeedbackText != null)
            {
                copyFeedbackText.gameObject.SetActive(false);
            }
        }

        private CharacterClass _selectedClass = CharacterClass.Lumberjack;

        /// <summary>
        /// Called automatically whenever a player joins, leaves, or updates state.
        /// </summary>
        public void RefreshPlayerList()
        {
            WoodsmenLobbyPlayer.SanitizePlayersList();

            // Confirmation layer: Catch any players that were spawned but missed the static list registration
            var scenePlayers = FindObjectsByType<WoodsmenLobbyPlayer>(FindObjectsSortMode.None);
            foreach (var p in scenePlayers)
            {
                if (p != null && !WoodsmenLobbyPlayer.AllPlayers.Contains(p))
                {
                    WoodsmenLobbyPlayer.AllPlayers.Add(p);
                }
            }

            var players = WoodsmenLobbyPlayer.AllPlayers;
            int count = players.Count;

            // Ensure container exists
            if (playerListContainer == null) return;

            if (count == 0)
            {
                bool isHost = Mirror.NetworkServer.active;
                if (isHost)
                {
                    if (playerCountText != null) playerCountText.text = "Players (1/2)";
                    EnsureSlotCount(1);
                    _activeSlots[0].gameObject.SetActive(true);
                    _activeSlots[0].BindDirect("Host (You)", _selectedClass, isHost: true, isReady: true, isLocal: true);
                }
                else
                {
                    if (playerCountText != null) playerCountText.text = "Connecting to Host...";
                    EnsureSlotCount(1);
                    _activeSlots[0].gameObject.SetActive(true);
                    _activeSlots[0].BindDirect("Waiting for Host...", _selectedClass, isHost: false, isReady: false, isLocal: true);
                }

                for (int i = 1; i < _activeSlots.Count; i++)
                {
                    _activeSlots[i].Clear();
                }
            }
            else
            {
                if (playerCountText != null) playerCountText.text = $"Players ({count}/2)";
                EnsureSlotCount(count);

                for (int i = 0; i < _activeSlots.Count; i++)
                {
                    if (i < count && players[i] != null)
                    {
                        _activeSlots[i].gameObject.SetActive(true);
                        var player = players[i];
                        bool isLocal = player == WoodsmenLobbyPlayer.LocalPlayer;
                        _activeSlots[i].Bind(player, isLocal);
                    }
                    else
                    {
                        _activeSlots[i].Clear();
                    }
                }
            }

            UpdateLocalPlayerClassUI();
            UpdateHostControls();
        }

        private void EnsureSlotCount(int count)
        {
            while (_activeSlots.Count < count)
            {
                GameObject slotGo;
                if (playerSlotPrefab != null)
                {
                    slotGo = Instantiate(playerSlotPrefab, playerListContainer);
                }
                else
                {
                    slotGo = CreateProceduralSlotUI();
                    slotGo.transform.SetParent(playerListContainer, false);
                }

                if (slotGo.TryGetComponent(out LobbyPlayerSlotUI slotUI))
                {
                    _activeSlots.Add(slotUI);
                }
            }
        }

        private bool _isShowingTemporaryFeedback = false;

        private void OnClassButtonClicked(CharacterClass characterClass)
        {
            // Disallow selecting a class already chosen by another player
            foreach (var player in WoodsmenLobbyPlayer.AllPlayers)
            {
                if (player != null && player != WoodsmenLobbyPlayer.LocalPlayer && player.SelectedClass == characterClass)
                {
                    ShowClassUnavailableFeedback(characterClass);
                    return;
                }
            }

            _selectedClass = characterClass;
            UpdateClassSelectionHighlight(characterClass);

            var local = WoodsmenLobbyPlayer.LocalPlayer;
            if (local != null)
            {
                local.CmdSelectClass(characterClass);
            }
            else
            {
                // Refresh local fallback slot immediately
                RefreshPlayerList();
            }
        }

        public void ShowClassUnavailableFeedback(CharacterClass unavailableClass)
        {
            if (selectedClassStatusText != null)
            {
                _isShowingTemporaryFeedback = true;
                selectedClassStatusText.text = $"<color=#FF4444><b>{unavailableClass.ToString().ToUpper()}</b> is already taken! Each player must choose a unique role.</color>";
                CancelInvoke(nameof(RevertClassStatusText));
                Invoke(nameof(RevertClassStatusText), 2.5f);
            }
        }

        private void RevertClassStatusText()
        {
            _isShowingTemporaryFeedback = false;
            UpdateLocalPlayerClassUI();
        }

        private void UpdateLocalPlayerClassUI()
        {
            var local = WoodsmenLobbyPlayer.LocalPlayer;
            if (local != null)
            {
                _selectedClass = local.SelectedClass;
            }
            UpdateClassSelectionHighlight(_selectedClass);
        }

        private void UpdateClassSelectionHighlight(CharacterClass characterClass)
        {
            bool isLumberjack = characterClass == CharacterClass.Lumberjack;
            bool isWarrior = characterClass == CharacterClass.Warrior;

            bool isLumberjackTakenByOther = false;
            bool isWarriorTakenByOther = false;

            foreach (var player in WoodsmenLobbyPlayer.AllPlayers)
            {
                if (player != null && player != WoodsmenLobbyPlayer.LocalPlayer)
                {
                    if (player.SelectedClass == CharacterClass.Lumberjack)
                        isLumberjackTakenByOther = true;
                    else if (player.SelectedClass == CharacterClass.Warrior)
                        isWarriorTakenByOther = true;
                }
            }

            // Lock out buttons taken by the other player
            if (selectLumberjackButton != null)
            {
                selectLumberjackButton.interactable = !isLumberjackTakenByOther;
            }
            if (selectWarriorButton != null)
            {
                selectWarriorButton.interactable = !isWarriorTakenByOther;
            }

            if (lumberjackHighlight != null) lumberjackHighlight.gameObject.SetActive(isLumberjack);
            if (warriorHighlight != null) warriorHighlight.gameObject.SetActive(isWarrior);

            if (selectedClassStatusText != null && !_isShowingTemporaryFeedback)
            {
                if (isWarriorTakenByOther && characterClass == CharacterClass.Lumberjack)
                {
                    selectedClassStatusText.text = "You: <color=#FFAA33><b>LUMBERJACK</b></color> | Teammate: <color=#FF5555><b>WARRIOR</b></color>";
                }
                else if (isLumberjackTakenByOther && characterClass == CharacterClass.Warrior)
                {
                    selectedClassStatusText.text = "You: <color=#FF5555><b>WARRIOR</b></color> | Teammate: <color=#FFAA33><b>LUMBERJACK</b></color>";
                }
                else
                {
                    selectedClassStatusText.text = isWarrior
                        ? "Selected: <color=#FF5555><b>WARRIOR</b></color> (Melee Combat)"
                        : "Selected: <color=#FFAA33><b>LUMBERJACK</b></color> (Resource Gathering)";
                }
            }
        }

        private void UpdateHostControls()
        {
            var local = WoodsmenLobbyPlayer.LocalPlayer;
            bool isHost = local != null ? local.IsHost : Mirror.NetworkServer.active;

            if (startMatchButton != null)
            {
                startMatchButton.gameObject.SetActive(isHost);
                if (isHost && startMatchButtonText != null)
                {
                    startMatchButtonText.text = "START MATCH";
                }
            }
        }

        private void OnStartMatchClicked()
        {
            Debug.Log("[LobbyRoomUI] Host clicked Start Match. Changing scene to Gameplay Scene...");
            if (WoodsmenNetworkManager.Instance != null && Mirror.NetworkServer.active)
            {
                WoodsmenNetworkManager.Instance.StartGame();
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("Gameplay Scene");
            }
        }

        private void OnLeaveRoomClicked()
        {
            Debug.Log("[LobbyRoomUI] Leaving room...");

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
                    Debug.LogWarning($"[LobbyRoomUI] Exception while sending leave notification: {ex.Message}");
                }
            }

            if (WoodsmenNetworkManager.Instance != null)
            {
                WoodsmenNetworkManager.Instance.LeaveRoom();
            }
            else if (MainMenuManager.Instance != null)
            {
                MainMenuManager.Instance.ReturnToMainMenu();
            }
        }

        private GameObject CreateProceduralSlotUI()
        {
            GameObject slot = new GameObject("LobbyPlayerSlot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = slot.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 50f);

            var img = slot.GetComponent<Image>();
            img.color = new Color(0.12f, 0.15f, 0.18f, 0.9f);

            // Add text child
            GameObject textObj = new GameObject("PlayerName", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(slot.transform, false);
            var textRt = textObj.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.05f, 0f);
            textRt.anchorMax = new Vector2(0.6f, 1f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textObj.GetComponent<TextMeshProUGUI>();
            tmp.text = "Player";
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.fontSize = 20f;
            tmp.color = Color.white;

            // Add class text child
            GameObject classObj = new GameObject("ClassBadge", typeof(RectTransform), typeof(TextMeshProUGUI));
            classObj.transform.SetParent(slot.transform, false);
            var classRt = classObj.GetComponent<RectTransform>();
            classRt.anchorMin = new Vector2(0.65f, 0f);
            classRt.anchorMax = new Vector2(0.95f, 1f);
            classRt.offsetMin = Vector2.zero;
            classRt.offsetMax = Vector2.zero;

            var classTmp = classObj.GetComponent<TextMeshProUGUI>();
            classTmp.text = "LUMBERJACK";
            classTmp.alignment = TextAlignmentOptions.MidlineRight;
            classTmp.fontSize = 18f;
            classTmp.color = new Color(0.85f, 0.55f, 0.2f, 1f);

            var slotUI = slot.AddComponent<LobbyPlayerSlotUI>();
            // Use reflection or serialized fields via helper
            var fieldName = typeof(LobbyPlayerSlotUI).GetField("playerNameText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fieldName?.SetValue(slotUI, tmp);
            var fieldClass = typeof(LobbyPlayerSlotUI).GetField("classBadgeText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fieldClass?.SetValue(slotUI, classTmp);
            var fieldBg = typeof(LobbyPlayerSlotUI).GetField("backgroundImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fieldBg?.SetValue(slotUI, img);

            return slot;
        }
    }
}
