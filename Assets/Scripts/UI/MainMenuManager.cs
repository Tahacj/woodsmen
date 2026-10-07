using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Woodsmen.Networking;
using JustAGame;
using JustAGame.Core.Platform;
using JustAGame.Core.Network;
using JustAGame.Pooling;
#if PRIMETWEEN_INSTALLED || UNITY_EDITOR
using PrimeTween;
#endif

namespace Woodsmen.UI
{
    /// <summary>
    /// Master UI controller for the Main Menu scene.
    /// Manages view state transitions:
    /// 1. Main View (Start, Exit buttons)
    /// 2. Modal View (Create / Join Room modal - hides Start & Exit while open)
    /// 3. Lobby Room View (Connected players list, class selection, join code)
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuManager : MonoBehaviour
    {
        public static MainMenuManager Instance { get; private set; }

        [Header("Main Menu Buttons")]
        [Tooltip("The parent GameObject containing Start Button and Exit Button, or the buttons directly.")]
        [SerializeField] private GameObject mainMenuButtonsGroup;
        [SerializeField] private Button startButton;
        [SerializeField] private Button exitButton;

        [Header("Create / Join Modal")]
        [Tooltip("The Create/Join Room modal GameObject.")]
        [SerializeField] private GameObject createJoinModal;
        [SerializeField] private Button closeModalButton;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private Button joinRoomButton;
        [SerializeField] private TMP_InputField joinCodeInputField;
        [SerializeField] private TMP_Text createFeedbackText;
        [SerializeField] private TMP_Text joinFeedbackText;

        [Header("Lobby / Room Window")]
        [Tooltip("The active room lobby panel showing connected players and class pick.")]
        [SerializeField] private GameObject lobbyRoomWindow;

        [Header("Animation Settings")]
        [SerializeField] private bool useAnimations = true;
        [SerializeField] private float animationDuration = 0.25f;

        // Events for network systems to subscribe to
        public event Action OnCreateRoomClicked;
        public event Action<string> OnJoinRoomClicked;
        public event Action OnLeaveRoomClicked;

        private CanvasGroup _modalCanvasGroup;
        private CanvasGroup _mainButtonsCanvasGroup;
        private bool _isCreatingRoom = false;
        private bool _isJoiningRoom = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureNetworkManagerExists();
            AutoDiscoverReferencesIfMissing();
            EnsureCanvasGroups();
            SetupButtonListeners();

            Debug.Log($"[MainMenuManager] Awake complete on '{gameObject.name}'. StartButton: {(startButton != null ? startButton.name : "NULL")}, Modal: {(createJoinModal != null ? createJoinModal.name : "NULL")}");
        }

        /// <summary>
        /// Ensures NetworkManager and EOSManager exist in the scene.
        /// </summary>
        public static void EnsureNetworkManagerExists()
        {
            try
            {
                if (WoodsmenNetworkManager.Instance != null && EOSNetworkManagerBridge.Instance != null)
                {
                    return;
                }

                GameObject nmGo = GameObject.Find("NetworkManager");
                if (nmGo == null)
                {
                    var prefab = Resources.Load<GameObject>("Network/NetworkManager");
                    if (prefab != null)
                    {
                        nmGo = Instantiate(prefab);
                        nmGo.name = "NetworkManager";
                        Debug.Log("[MainMenuManager] Instantiated NetworkManager from Resources/Network/NetworkManager.");
                    }
                    else
                    {
                        nmGo = new GameObject("NetworkManager");
                    }
                }

                nmGo.transform.SetParent(null);
                DontDestroyOnLoad(nmGo);
                var netMan = nmGo.GetComponent<WoodsmenNetworkManager>();
                if (netMan == null) netMan = nmGo.AddComponent<WoodsmenNetworkManager>();

                netMan.dontDestroyOnLoad = true;
                netMan.runInBackground = true;
                netMan.EnsurePrefabsAssigned();

                // Ensure EOSManager exists if not already present
                if (EOSNetworkManagerBridge.Instance == null)
                {
                    var existingEos = GameObject.Find("EOSManager");
                    if (existingEos != null)
                    {
                        existingEos.transform.SetParent(null);
                        DontDestroyOnLoad(existingEos);
                        var bridge = existingEos.GetComponent<EOSNetworkManagerBridge>();
                        if (bridge != null) bridge.ResolveReferences();
                    }
                    else
                    {
                        var eosPrefab = Resources.Load<GameObject>("Network/EOSManager");
                        if (eosPrefab != null)
                        {
                            var eosGo = Instantiate(eosPrefab);
                            eosGo.name = "EOSManager";
                            eosGo.transform.SetParent(null);
                            DontDestroyOnLoad(eosGo);
                            Debug.Log("[MainMenuManager] Instantiated EOSManager from Resources/Network/EOSManager.");
                        }
                    }
                }

                Debug.Log("[MainMenuManager] Validated network infrastructure in scene.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MainMenuManager] Exception in EnsureNetworkManagerExists: {ex.Message}");
            }
        }

        private void AutoDiscoverReferencesIfMissing()
        {
            // Auto-locate Start Button
            if (startButton == null)
            {
                var btnGo = FindEverywhere("Start Button");
                if (btnGo != null) startButton = btnGo.GetComponent<Button>();
            }

            // Auto-locate Exit Button
            if (exitButton == null)
            {
                var btnGo = FindEverywhere("Exit");
                if (btnGo != null) exitButton = btnGo.GetComponent<Button>();
            }

            // Auto-locate Main Buttons Group
            if (mainMenuButtonsGroup == null)
            {
                mainMenuButtonsGroup = FindEverywhere("Main Buttons Group");
            }

            // Auto-locate Create/Join Modal (finds active AND inactive objects)
            if (createJoinModal == null)
            {
                createJoinModal = FindEverywhere("Create/Join Room");
            }

            // Auto-locate Close Modal Button
            if (closeModalButton == null && createJoinModal != null)
            {
                var closeTrans = FindChildRecursive(createJoinModal.transform, "Close Modal Button");
                if (closeTrans != null) closeModalButton = closeTrans.GetComponent<Button>();
            }

            // Auto-locate Create Room Button
            if (createRoomButton == null && createJoinModal != null)
            {
                var createSection = FindChildRecursive(createJoinModal.transform, "Create Section");
                if (createSection != null) createRoomButton = createSection.GetComponentInChildren<Button>(true);
            }

            // Auto-locate Join Room Button & Input Field
            if (createJoinModal != null)
            {
                var joinSection = FindChildRecursive(createJoinModal.transform, "Join Section");
                if (joinSection != null)
                {
                    if (joinRoomButton == null) joinRoomButton = joinSection.GetComponentInChildren<Button>(true);
                    if (joinCodeInputField == null) joinCodeInputField = joinSection.GetComponentInChildren<TMP_InputField>(true);
                }
            }

            // Auto-locate Lobby Room Window
            if (lobbyRoomWindow == null)
            {
                lobbyRoomWindow = FindEverywhere("Room Lobby Window");
            }

            // Auto-locate Create and Join Feedback Texts
            if (createJoinModal != null)
            {
                if (createFeedbackText == null)
                {
                    var cfb = FindChildRecursive(createJoinModal.transform, "CreateFeedbackText");
                    if (cfb != null) createFeedbackText = cfb.GetComponent<TMP_Text>();
                }

                if (joinFeedbackText == null)
                {
                    var jfb = FindChildRecursive(createJoinModal.transform, "JoinFeedbackText");
                    if (jfb != null) joinFeedbackText = jfb.GetComponent<TMP_Text>();
                }

                // Clean up legacy ModalFeedbackText if present
                var oldFb = FindChildRecursive(createJoinModal.transform, "ModalFeedbackText");
                if (oldFb != null && oldFb.gameObject != null)
                {
                    Destroy(oldFb.gameObject);
                }
            }
        }

        /// <summary>
        /// Finds a GameObject by name in the active scene, even if it is inactive or at root level.
        /// </summary>
        public static GameObject FindEverywhere(string name)
        {
            // 1. Try active first
            var active = GameObject.Find(name);
            if (active != null) return active;

            // 2. Search active scene root objects and all inactive children
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.isLoaded)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return root;

                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return t.gameObject;
                    }
                }
            }

            // 3. Fallback to Resources search
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.name.Equals(name, StringComparison.OrdinalIgnoreCase) && go.scene.isLoaded)
                {
                    return go;
                }
            }

            return null;
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent == null) return null;
            if (parent.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var found = FindChildRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private void Start()
        {
            // Safety check: MainMenuManager must NEVER be on the modal it hides!
            if (createJoinModal != null && gameObject == createJoinModal)
            {
                Debug.LogError("[MainMenuManager] CRITICAL SETUP ERROR: MainMenuManager is attached directly to 'Create/Join Room' modal! It must be attached to 'Main Menu Canvas'.");
            }

            // Initial state: Show main buttons, hide modal and lobby room
            ShowMainButtonsImmediate();
            if (createJoinModal != null && createJoinModal != gameObject)
            {
                createJoinModal.SetActive(false);
            }
            if (lobbyRoomWindow != null) lobbyRoomWindow.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RemoveButtonListeners();
        }

        private void EnsureCanvasGroups()
        {
            if (createJoinModal != null && !createJoinModal.TryGetComponent(out _modalCanvasGroup))
            {
                _modalCanvasGroup = createJoinModal.AddComponent<CanvasGroup>();
            }

            if (mainMenuButtonsGroup != null && !mainMenuButtonsGroup.TryGetComponent(out _mainButtonsCanvasGroup))
            {
                _mainButtonsCanvasGroup = mainMenuButtonsGroup.AddComponent<CanvasGroup>();
            }
        }

        private static bool HasPersistentListener(Button.ButtonClickedEvent onClick, string methodName)
        {
            if (onClick == null) return false;
            int count = onClick.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                if (onClick.GetPersistentMethodName(i) == methodName)
                    return true;
            }
            return false;
        }

        private void SetupButtonListeners()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(OpenCreateJoinModal);
                if (!HasPersistentListener(startButton.onClick, nameof(OpenCreateJoinModal)))
                {
                    startButton.onClick.AddListener(OpenCreateJoinModal);
                }
                Debug.Log("[MainMenuManager] Successfully hooked listener to Start Button!");
            }
            else
            {
                Debug.LogError("[MainMenuManager] Start Button reference is MISSING!");
            }

            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(QuitGame);
                if (!HasPersistentListener(exitButton.onClick, nameof(QuitGame)))
                {
                    exitButton.onClick.AddListener(QuitGame);
                }
            }

            if (closeModalButton != null)
            {
                closeModalButton.onClick.RemoveListener(CloseCreateJoinModal);
                if (!HasPersistentListener(closeModalButton.onClick, nameof(CloseCreateJoinModal)))
                {
                    closeModalButton.onClick.AddListener(CloseCreateJoinModal);
                }
            }

            if (createRoomButton != null)
            {
                createRoomButton.onClick.RemoveListener(HandleCreateRoomClick);
                if (!HasPersistentListener(createRoomButton.onClick, nameof(HandleCreateRoomClick)))
                {
                    createRoomButton.onClick.AddListener(HandleCreateRoomClick);
                }
            }

            if (joinRoomButton != null)
            {
                joinRoomButton.onClick.RemoveListener(HandleJoinRoomClick);
                if (!HasPersistentListener(joinRoomButton.onClick, nameof(HandleJoinRoomClick)))
                {
                    joinRoomButton.onClick.AddListener(HandleJoinRoomClick);
                }
            }

            if (joinCodeInputField != null)
            {
                joinCodeInputField.onValueChanged.RemoveListener(OnJoinCodeInputChanged);
                joinCodeInputField.onValueChanged.AddListener(OnJoinCodeInputChanged);
            }
        }

        private void RemoveButtonListeners()
        {
            if (startButton != null) startButton.onClick.RemoveListener(OpenCreateJoinModal);
            if (exitButton != null) exitButton.onClick.RemoveListener(QuitGame);
            if (closeModalButton != null) closeModalButton.onClick.RemoveListener(CloseCreateJoinModal);
            if (createRoomButton != null) createRoomButton.onClick.RemoveListener(HandleCreateRoomClick);
            if (joinRoomButton != null) joinRoomButton.onClick.RemoveListener(HandleJoinRoomClick);
            if (joinCodeInputField != null) joinCodeInputField.onValueChanged.RemoveListener(OnJoinCodeInputChanged);
        }

        private void OnJoinCodeInputChanged(string text)
        {
            ClearJoinFeedback();
        }

        #region View Transitions

        /// <summary>
        /// Opens the Create/Join Room modal and hides the Start & Exit buttons.
        /// </summary>
        public void OpenCreateJoinModal()
        {
            Debug.Log("[MainMenuManager] OpenCreateJoinModal invoked!");

            // 1. Hide main menu buttons
            SetMainButtonsVisibility(false);

            // 2. Ensure modal reference is bound
            if (createJoinModal == null)
            {
                createJoinModal = FindEverywhere("Create/Join Room");
            }

            // 3. Show create/join modal
            if (createJoinModal != null)
            {
                if (_modalCanvasGroup == null)
                {
                    if (!createJoinModal.TryGetComponent(out _modalCanvasGroup))
                        _modalCanvasGroup = createJoinModal.AddComponent<CanvasGroup>();
                }

                createJoinModal.SetActive(true);

                if (useAnimations && _modalCanvasGroup != null)
                {
                    _modalCanvasGroup.alpha = 0f;
                    createJoinModal.transform.localScale = Vector3.one * 0.9f;
#if PRIMETWEEN_INSTALLED
                    Tween.Alpha(_modalCanvasGroup, 1f, animationDuration, Ease.OutQuad);
                    Tween.Scale(createJoinModal.transform, Vector3.one, animationDuration, Ease.OutBack);
#else
                    _modalCanvasGroup.alpha = 1f;
                    createJoinModal.transform.localScale = Vector3.one;
#endif
                }
                else if (_modalCanvasGroup != null)
                {
                    _modalCanvasGroup.alpha = 1f;
                    createJoinModal.transform.localScale = Vector3.one;
                }

                Debug.Log($"[MainMenuManager] OpenCreateJoinModal: Successfully activated '{createJoinModal.name}'. ActiveSelf: {createJoinModal.activeSelf}");

                // Ensure modal buttons are bound and listening
                if (createRoomButton == null)
                {
                    var btnGo = FindEverywhere("Create Room Button");
                    if (btnGo != null) createRoomButton = btnGo.GetComponent<Button>();
                }
                if (createRoomButton != null)
                {
                    createRoomButton.onClick.RemoveListener(HandleCreateRoomClick);
                    if (!HasPersistentListener(createRoomButton.onClick, nameof(HandleCreateRoomClick)))
                    {
                        createRoomButton.onClick.AddListener(HandleCreateRoomClick);
                    }
                }

                if (joinRoomButton == null)
                {
                    var btnGo = FindEverywhere("Join Button");
                    if (btnGo != null) joinRoomButton = btnGo.GetComponent<Button>();
                }
                if (joinRoomButton != null)
                {
                    joinRoomButton.onClick.RemoveListener(HandleJoinRoomClick);
                    if (!HasPersistentListener(joinRoomButton.onClick, nameof(HandleJoinRoomClick)))
                    {
                        joinRoomButton.onClick.AddListener(HandleJoinRoomClick);
                    }
                }

                if (closeModalButton != null)
                {
                    closeModalButton.onClick.RemoveListener(CloseCreateJoinModal);
                    if (!HasPersistentListener(closeModalButton.onClick, nameof(CloseCreateJoinModal)))
                    {
                        closeModalButton.onClick.AddListener(CloseCreateJoinModal);
                    }
                }

                // Reset modal state
                ClearAllFeedback();
                SetModalButtonsInteractable(true);
                SetJoinRoomButtonText("JOIN");
                SetCreateRoomButtonText("CREATE ROOM");
                if (joinCodeInputField != null)
                {
                    joinCodeInputField.text = "";
                }
            }
            else
            {
                Debug.LogError("[MainMenuManager] OpenCreateJoinModal FAILED: 'Create/Join Room' GameObject was not found in the scene!");
            }
        }

        /// <summary>
        /// Closes the modal and restores the Start & Exit buttons.
        /// </summary>
        public void CloseCreateJoinModal()
        {
            if (createJoinModal != null)
            {
                if (useAnimations && _modalCanvasGroup != null)
                {
#if PRIMETWEEN_INSTALLED
                    Tween.Alpha(_modalCanvasGroup, 0f, animationDuration * 0.8f, Ease.InQuad)
                        .OnComplete(() =>
                        {
                            if (createJoinModal != null) createJoinModal.SetActive(false);
                            SetMainButtonsVisibility(true);
                        });
                    return;
#else
                    _modalCanvasGroup.alpha = 0f;
                    if (createJoinModal != null) createJoinModal.SetActive(false);
                    SetMainButtonsVisibility(true);
                    return;
#endif
                }
                else
                {
                    createJoinModal.SetActive(false);
                }
            }

            SetMainButtonsVisibility(true);
        }

        /// <summary>
        /// Switches to the Lobby Room window when a room is created or joined.
        /// </summary>
        public void ShowLobbyRoomWindow()
        {
            Debug.Log("[MainMenuManager] ShowLobbyRoomWindow called.");

            if (lobbyRoomWindow == null)
            {
                lobbyRoomWindow = FindEverywhere("Room Lobby Window");
            }

            // Ensure Room Lobby Window is attached to root Main Menu Canvas, NOT Create/Join Room
            var canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
            if (lobbyRoomWindow != null && canvas != null && lobbyRoomWindow.transform.parent != canvas.transform)
            {
                lobbyRoomWindow.transform.SetParent(canvas.transform, false);
            }

            if (createJoinModal != null) createJoinModal.SetActive(false);
            SetMainButtonsVisibility(false);

            if (lobbyRoomWindow != null)
            {
                lobbyRoomWindow.layer = 5;
                foreach (var t in lobbyRoomWindow.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = 5;
                }

                lobbyRoomWindow.SetActive(true);
                if (lobbyRoomWindow.TryGetComponent(out CanvasGroup cg))
                {
                    cg.alpha = 0f;
                    lobbyRoomWindow.transform.localScale = Vector3.one * 0.95f;
#if PRIMETWEEN_INSTALLED
                    Tween.Alpha(cg, 1f, animationDuration, Ease.OutQuad);
                    Tween.Scale(lobbyRoomWindow.transform, Vector3.one, animationDuration, Ease.OutBack);
#else
                    cg.alpha = 1f;
                    lobbyRoomWindow.transform.localScale = Vector3.one;
#endif
                }
                Debug.Log("[MainMenuManager] ShowLobbyRoomWindow: Activated Room Lobby Window!");
            }
            else
            {
                Debug.LogError("[MainMenuManager] ShowLobbyRoomWindow FAILED: 'Room Lobby Window' was not found!");
            }
        }

        private bool _isReturningToMainMenu = false;

        /// <summary>
        /// Returns back to the Main Menu from the lobby (e.g. on disconnect or leave room).
        /// </summary>
        public void ReturnToMainMenu()
        {
            if (_isReturningToMainMenu) return;
            try
            {
                _isReturningToMainMenu = true;

                if (lobbyRoomWindow != null) lobbyRoomWindow.SetActive(false);
                if (createJoinModal != null) createJoinModal.SetActive(false);
                SetMainButtonsVisibility(true);

                WoodsmenLobbyPlayer.ResetLobbyData();

                if (GamePlatform.Network != null)
                {
                    GamePlatform.Network.StopSession();
                }

                OnLeaveRoomClicked?.Invoke();
            }
            finally
            {
                _isReturningToMainMenu = false;
            }
        }

        public void SetMainButtonsVisibility(bool visible)
        {
            if (mainMenuButtonsGroup != null)
            {
                if (visible)
                {
                    mainMenuButtonsGroup.SetActive(true);
                    if (useAnimations && _mainButtonsCanvasGroup != null)
                    {
                        _mainButtonsCanvasGroup.alpha = 0f;
#if PRIMETWEEN_INSTALLED
                        Tween.Alpha(_mainButtonsCanvasGroup, 1f, animationDuration, Ease.OutQuad);
#else
                        _mainButtonsCanvasGroup.alpha = 1f;
#endif
                    }
                    else if (_mainButtonsCanvasGroup != null)
                    {
                        _mainButtonsCanvasGroup.alpha = 1f;
                    }
                }
                else
                {
                    if (useAnimations && _mainButtonsCanvasGroup != null)
                    {
#if PRIMETWEEN_INSTALLED
                        Tween.Alpha(_mainButtonsCanvasGroup, 0f, animationDuration * 0.7f, Ease.InQuad)
                            .OnComplete(() =>
                            {
                                if (mainMenuButtonsGroup != null) mainMenuButtonsGroup.SetActive(false);
                            });
#else
                        _mainButtonsCanvasGroup.alpha = 0f;
                        if (mainMenuButtonsGroup != null) mainMenuButtonsGroup.SetActive(false);
#endif
                    }
                    else
                    {
                        mainMenuButtonsGroup.SetActive(false);
                    }
                }
            }
            else
            {
                // Fallback if buttons are not grouped under a parent
                if (startButton != null) startButton.gameObject.SetActive(visible);
                if (exitButton != null) exitButton.gameObject.SetActive(visible);
            }
        }

        private void ShowMainButtonsImmediate()
        {
            if (mainMenuButtonsGroup != null)
            {
                mainMenuButtonsGroup.SetActive(true);
                if (_mainButtonsCanvasGroup != null) _mainButtonsCanvasGroup.alpha = 1f;
            }
            else
            {
                if (startButton != null) startButton.gameObject.SetActive(true);
                if (exitButton != null) exitButton.gameObject.SetActive(true);
            }
        }

        #endregion

        #region Modal Feedback Helpers

        private TMP_Text GetOrCreateCreateFeedbackText()
        {
            if (createFeedbackText != null) return createFeedbackText;

            if (createJoinModal != null)
            {
                var createSection = FindChildRecursive(createJoinModal.transform, "Create Section");
                Transform parent = createSection != null ? createSection : createJoinModal.transform;

                var found = FindChildRecursive(parent, "CreateFeedbackText");
                if (found != null)
                {
                    createFeedbackText = found.GetComponent<TMP_Text>();
                    return createFeedbackText;
                }

                GameObject fbGo = new GameObject("CreateFeedbackText", typeof(RectTransform), typeof(TextMeshProUGUI));
                fbGo.transform.SetParent(parent, false);
                var rt = fbGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, -182f);
                rt.sizeDelta = new Vector2(360f, 32f);

                var tmp = fbGo.GetComponent<TextMeshProUGUI>();
                tmp.fontSize = 15f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = true;
                tmp.color = new Color(1f, 0.35f, 0.35f, 1f);
                tmp.text = "";
                createFeedbackText = tmp;
            }
            return createFeedbackText;
        }

        private TMP_Text GetOrCreateJoinFeedbackText()
        {
            if (joinFeedbackText != null) return joinFeedbackText;

            if (createJoinModal != null)
            {
                var joinSection = FindChildRecursive(createJoinModal.transform, "Join Section");
                Transform parent = joinSection != null ? joinSection : createJoinModal.transform;

                // Clean up legacy ModalFeedbackText if present
                var oldFb = FindChildRecursive(createJoinModal.transform, "ModalFeedbackText");
                if (oldFb != null && oldFb.gameObject != null)
                {
                    Destroy(oldFb.gameObject);
                }

                var found = FindChildRecursive(parent, "JoinFeedbackText");
                if (found != null)
                {
                    joinFeedbackText = found.GetComponent<TMP_Text>();
                    return joinFeedbackText;
                }

                GameObject fbGo = new GameObject("JoinFeedbackText", typeof(RectTransform), typeof(TextMeshProUGUI));
                fbGo.transform.SetParent(parent, false);
                var rt = fbGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, -182f);
                rt.sizeDelta = new Vector2(360f, 32f);

                var tmp = fbGo.GetComponent<TextMeshProUGUI>();
                tmp.fontSize = 15f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = true;
                tmp.color = new Color(1f, 0.35f, 0.35f, 1f);
                tmp.text = "";
                joinFeedbackText = tmp;
            }
            return joinFeedbackText;
        }

        public void ShowCreateError(string message)
        {
            var fb = GetOrCreateCreateFeedbackText();
            if (fb != null)
            {
                fb.gameObject.SetActive(true);
                fb.color = new Color(1f, 0.35f, 0.35f, 1f);
                fb.text = message;
            }
        }

        public void ShowCreateStatus(string message)
        {
            var fb = GetOrCreateCreateFeedbackText();
            if (fb != null)
            {
                fb.gameObject.SetActive(true);
                fb.color = new Color(1f, 0.85f, 0.3f, 1f);
                fb.text = message;
            }
        }

        public void ClearCreateFeedback()
        {
            var fb = GetOrCreateCreateFeedbackText();
            if (fb != null)
            {
                fb.text = "";
                fb.gameObject.SetActive(false);
            }
        }

        public void ShowJoinError(string message)
        {
            var fb = GetOrCreateJoinFeedbackText();
            if (fb != null)
            {
                fb.gameObject.SetActive(true);
                fb.color = new Color(1f, 0.35f, 0.35f, 1f);
                fb.text = message;
            }
        }

        public void ShowJoinStatus(string message)
        {
            var fb = GetOrCreateJoinFeedbackText();
            if (fb != null)
            {
                fb.gameObject.SetActive(true);
                fb.color = new Color(1f, 0.85f, 0.3f, 1f);
                fb.text = message;
            }
        }

        public void ClearJoinFeedback()
        {
            var fb = GetOrCreateJoinFeedbackText();
            if (fb != null)
            {
                fb.text = "";
                fb.gameObject.SetActive(false);
            }
        }

        public void ClearAllFeedback()
        {
            ClearCreateFeedback();
            ClearJoinFeedback();
        }

        private void SetModalButtonsInteractable(bool interactable)
        {
            if (joinRoomButton != null) joinRoomButton.interactable = interactable;
            if (createRoomButton != null) createRoomButton.interactable = interactable;
            if (closeModalButton != null) closeModalButton.interactable = interactable;
            if (joinCodeInputField != null) joinCodeInputField.interactable = interactable;
        }

        private void SetJoinRoomButtonText(string text)
        {
            if (joinRoomButton != null)
            {
                var tmp = joinRoomButton.GetComponentInChildren<TMP_Text>();
                if (tmp != null) tmp.text = text;
            }
        }

        private void SetCreateRoomButtonText(string text)
        {
            if (createRoomButton != null)
            {
                var tmp = createRoomButton.GetComponentInChildren<TMP_Text>();
                if (tmp != null) tmp.text = text;
            }
        }

        #endregion

        #region Actions

        public async void HandleCreateRoomClick()
        {
            if (_isCreatingRoom)
            {
                Debug.LogWarning("[MainMenuManager] Create Room already in progress. Ignoring duplicate call.");
                return;
            }

            _isCreatingRoom = true;
            try
            {
                Debug.Log("[MainMenuManager] Create Room clicked!");

                EnsureNetworkManagerExists();

                SetModalButtonsInteractable(false);
                SetCreateRoomButtonText("CREATING...");
                ClearCreateFeedback();

                string finalRoomCode = null;
                string createError = null;

                if (GamePlatform.NetworkManager != null)
                {
                    ShowCreateStatus("STARTING EOS REMOTE HOST...");
                    NetworkStartResult result = await GamePlatform.NetworkManager.StartRemote();
                    if (result == NetworkStartResult.Success)
                    {
                        finalRoomCode = GamePlatform.NetworkManager.GetCode();
                    }
                    else
                    {
                        createError = $"FAILED TO CREATE EOS ROOM: {result}";
                    }
                }
                else if (WoodsmenNetworkManager.Instance != null)
                {
                    finalRoomCode = UnityEngine.Random.Range(1000, 10000).ToString();
                    WoodsmenNetworkManager.Instance.HandleCreateRoom(finalRoomCode);
                }

                SetModalButtonsInteractable(true);
                SetCreateRoomButtonText("CREATE ROOM");

                if (string.IsNullOrEmpty(finalRoomCode))
                {
                    ShowCreateError(string.IsNullOrEmpty(createError)
                        ? "FAILED TO CREATE ONLINE ROOM. CHECK INTERNET CONNECTION."
                        : createError.ToUpperInvariant());
                    return;
                }

                // Successfully allocated room! Open Lobby Room Window
                ClearAllFeedback();
                ShowLobbyRoomWindow();

                if (LobbyRoomUI.Instance != null)
                {
                    LobbyRoomUI.Instance.SetRoomCode(finalRoomCode);
                    LobbyRoomUI.Instance.RefreshPlayerList();
                }

                OnCreateRoomClicked?.Invoke();
            }
            finally
            {
                _isCreatingRoom = false;
            }
        }

        public async void HandleJoinRoomClick()
        {
            if (_isJoiningRoom)
            {
                Debug.LogWarning("[MainMenuManager] Join Room already in progress. Ignoring duplicate call.");
                return;
            }

            _isJoiningRoom = true;
            try
            {
                string code = joinCodeInputField != null ? joinCodeInputField.text.Trim() : string.Empty;
                if (string.IsNullOrEmpty(code))
                {
                    Debug.LogWarning("[MainMenuManager] Cannot join room: invitation code is empty.");
                    ShowJoinError("PLEASE ENTER INVITATION CODE");
                    return;
                }

                bool isLocal = code.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) ||
                               code.Equals("LOCALHOST", StringComparison.OrdinalIgnoreCase) ||
                               code.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

                if (!isLocal && code.Length != 32)
                {
                    ShowJoinError("EOS CODE MUST BE 32 CHARACTERS (PUID)");
                    return;
                }

                Debug.Log($"[MainMenuManager] Join Room clicked with code: {code}");

                EnsureNetworkManagerExists();

                // Provide loading state on modal WITHOUT opening the Lobby Window prematurely
                SetModalButtonsInteractable(false);
                SetJoinRoomButtonText("CONNECTING...");
                ShowJoinStatus($"CONNECTING TO {code}...");
                ClearCreateFeedback();

                bool success = false;
                string error = null;

                if (isLocal)
                {
                    if (GamePlatform.NetworkManager != null)
                    {
                        GamePlatform.NetworkManager.ConnectLocal("localhost");
                        success = true;
                    }
                    else if (WoodsmenNetworkManager.Instance != null)
                    {
                        WoodsmenNetworkManager.Instance.HandleJoinRoom(code);
                        success = true;
                    }
                }
                else if (GamePlatform.NetworkManager != null)
                {
                    NetworkStartResult result = await GamePlatform.NetworkManager.JoinRemote(code);
                    if (result == NetworkStartResult.Success)
                    {
                        success = true;
                    }
                    else
                    {
                        error = $"FAILED TO JOIN EOS ROOM: {result}";
                    }
                }
                else if (WoodsmenNetworkManager.Instance != null)
                {
                    WoodsmenNetworkManager.Instance.HandleJoinRoom(code);
                    success = true;
                }

                SetModalButtonsInteractable(true);
                SetJoinRoomButtonText("JOIN");

                if (!success)
                {
                    Debug.LogWarning($"[MainMenuManager] Failed to join room '{code}': {error}");
                    // DO NOT CREATE OR OPEN ROOM! STAY ON MODAL AND SHOW ERROR!
                    ShowJoinError(string.IsNullOrEmpty(error) ? "ROOM NOT FOUND OR EXPIRED" : error.ToUpperInvariant());
                    return;
                }

                // Connection confirmed! Open Lobby Room Window
                ClearAllFeedback();
                ShowLobbyRoomWindow();

                if (LobbyRoomUI.Instance != null)
                {
                    LobbyRoomUI.Instance.SetRoomCode(code);
                    LobbyRoomUI.Instance.RefreshPlayerList();
                }

                OnJoinRoomClicked?.Invoke(code);
            }
            finally
            {
                _isJoiningRoom = false;
            }
        }

        public void QuitGame()
        {
            Debug.Log("[MainMenuManager] Exiting game...");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        #endregion
    }
}
