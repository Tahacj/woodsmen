using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Woodsmen.Inventory.UI
{
    /// <summary>
    /// Master Canvas Controller for the Universal Character Inventory.
    /// Features:
    /// - Toggle open/close with [TAB] (Action Map) or [ESCAPE].
    /// - 24 Slot interactive grid with item icons, stack badges, and selection glow.
    /// - Item Details inspector with polymorphic item usage (consuming potions, etc.).
    /// - Mouse cursor state management and game input isolation while open.
    /// </summary>
    public class UniversalInventoryUI : MonoBehaviour
    {
        public static UniversalInventoryUI Instance { get; private set; }
        public static bool IsOpen { get; private set; }

        [Header("Window & Canvas References")]
        [SerializeField] private GameObject windowRoot;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform windowPanel;

        [Header("Header & Currency Display")]
        [SerializeField] private TMP_Text woodCounterText;
        [SerializeField] private TMP_Text goldCounterText;

        [Header("Inventory Grid & Details")]
        [SerializeField] private Transform slotGridContainer;
        [SerializeField] private InventoryItemDetailsUI detailsPanel;

        [Header("Close Button")]
        [SerializeField] private Button closeButton;

        private PlayerInventory _boundInventory;
        private readonly List<InventorySlotUI> _slotViews = new List<InventorySlotUI>();
        private int _selectedSlotIndex = -1;
        private Tween _fadeTween;
        private Tween _scaleTween;
        private int _lastToggleFrame = -1;
        private float _lastToggleTime = -1f;

        private void OnEnable()
        {
            Woodsmen.Players.PlayerInputReader.OnInventoryToggleRequested += HandleInventoryToggleRequested;
        }

        private void OnDisable()
        {
            Woodsmen.Players.PlayerInputReader.OnInventoryToggleRequested -= HandleInventoryToggleRequested;
        }

        private void HandleInventoryToggleRequested()
        {
            Toggle();
        }

        /// <summary>
        /// Global entry point to toggle the inventory UI.
        /// Searches for UniversalInventoryUI in the scene (even if inactive)
        /// and activates it to guarantee the window opens on Tab press.
        /// </summary>
        public static void ToggleGlobal()
        {
            if (Instance == null)
            {
                Instance = Object.FindAnyObjectByType<UniversalInventoryUI>(FindObjectsInactive.Include);
            }

            if (Instance != null)
            {
                if (!Instance.gameObject.activeSelf)
                {
                    Instance.gameObject.SetActive(true);
                }
                Instance.Toggle();
            }
            else
            {
                Debug.LogWarning("[Woodsmen] Tab was pressed, but no UniversalInventoryUI instance was found in the scene! Please check that your Canvas contains UniversalInventoryUI.");
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureComponents();
            SetupCloseButton();
        }

        private void Start()
        {
            Close(instant: true);
            TryBindPlayer();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_boundInventory != null)
            {
                _boundInventory.OnInventoryChanged -= HandleInventoryChanged;
            }
            if (_fadeTween.isAlive) _fadeTween.Stop();
            if (_scaleTween.isAlive) _scaleTween.Stop();
        }

        private void Update()
        {
            if (_boundInventory == null)
            {
                TryBindPlayer();
            }

            CheckInputToggle();
        }

        private void CheckInputToggle()
        {
            if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
        }

        public void Toggle()
        {
            if (Time.unscaledTime - _lastToggleTime < 0.15f) return;
            _lastToggleTime = Time.unscaledTime;

            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            IsOpen = true;
            if (windowRoot != null && windowRoot != gameObject) windowRoot.SetActive(true);
            if (windowPanel != null) windowPanel.gameObject.SetActive(true);

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (_fadeTween.isAlive) _fadeTween.Stop();
            if (_scaleTween.isAlive) _scaleTween.Stop();

            if (canvasGroup != null)
            {
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
                canvasGroup.alpha = 0f;
                _fadeTween = Tween.Alpha(canvasGroup, 1f, duration: 0.18f, ease: Ease.OutQuad);
            }

            if (windowPanel != null)
            {
                windowPanel.localScale = new Vector3(0.92f, 0.92f, 1f);
                _scaleTween = Tween.Scale(windowPanel, Vector3.one, duration: 0.20f, ease: Ease.OutBack);
            }

            RefreshAll();
            Debug.Log("<color=#10b981>[UniversalInventoryUI]</color> Inventory OPENED.");
        }

        public void Close(bool instant = false)
        {
            IsOpen = false;

            if (instant)
            {
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 0f;
                    canvasGroup.interactable = false;
                    canvasGroup.blocksRaycasts = false;
                }
                if (windowPanel != null && windowPanel.gameObject != gameObject)
                {
                    windowPanel.gameObject.SetActive(false);
                }
                if (windowRoot != null && windowRoot != gameObject)
                {
                    windowRoot.SetActive(false);
                }
                return;
            }

            if (_fadeTween.isAlive) _fadeTween.Stop();
            if (_scaleTween.isAlive) _scaleTween.Stop();

            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
                _fadeTween = Tween.Alpha(canvasGroup, 0f, duration: 0.14f, ease: Ease.InQuad)
                    .OnComplete(() =>
                    {
                        if (windowPanel != null && windowPanel.gameObject != gameObject)
                        {
                            windowPanel.gameObject.SetActive(false);
                        }
                        if (windowRoot != null && windowRoot != gameObject)
                        {
                            windowRoot.SetActive(false);
                        }
                    });
            }
            else
            {
                if (windowPanel != null && windowPanel.gameObject != gameObject)
                {
                    windowPanel.gameObject.SetActive(false);
                }
                if (windowRoot != null && windowRoot != gameObject)
                {
                    windowRoot.SetActive(false);
                }
            }

            Debug.Log("<color=#ef4444>[UniversalInventoryUI]</color> Inventory CLOSED.");
        }

        private void TryBindPlayer()
        {
            if (_boundInventory != null) return;

            if (PlayerInventory.LocalPlayerInstance != null)
            {
                _boundInventory = PlayerInventory.LocalPlayerInstance;
                _boundInventory.OnInventoryChanged += HandleInventoryChanged;

                if (detailsPanel != null) detailsPanel.Bind(_boundInventory);

                BuildSlotGrid();
                RefreshAll();
            }
        }

        private void HandleInventoryChanged()
        {
            RefreshAll();
        }

        public void RefreshAll()
        {
            if (_boundInventory == null) return;

            // 1. Refresh Currency
            if (woodCounterText != null)
            {
                woodCounterText.text = $"Wood: <b>{_boundInventory.Wood}</b>";
            }
            if (goldCounterText != null)
            {
                goldCounterText.text = $"Gold: <b>{_boundInventory.Money}</b>";
            }

            // 2. Refresh Slots
            var slots = _boundInventory.Slots;
            for (int i = 0; i < _slotViews.Count; i++)
            {
                if (i < slots.Count)
                {
                    _slotViews[i].gameObject.SetActive(true);
                    _slotViews[i].Bind(i, slots[i], i == _selectedSlotIndex, _boundInventory.CharacterClass);
                }
                else
                {
                    _slotViews[i].gameObject.SetActive(false);
                }
            }

            // 3. Refresh Details
            if (detailsPanel != null)
            {
                if (_selectedSlotIndex >= 0 && _selectedSlotIndex < slots.Count && !slots[_selectedSlotIndex].IsEmpty)
                {
                    detailsPanel.DisplayItem(slots[_selectedSlotIndex], _selectedSlotIndex);
                }
                else
                {
                    detailsPanel.Clear();
                }
            }
        }

        private void BuildSlotGrid()
        {
            if (slotGridContainer == null) return;

            // 1. If the user already created slot GameObjects with InventorySlotUI in the Editor, bind to them!
            InventorySlotUI[] existingSlots = slotGridContainer.GetComponentsInChildren<InventorySlotUI>(true);
            if (existingSlots != null && existingSlots.Length > 0 && _slotViews.Count == 0)
            {
                for (int i = 0; i < existingSlots.Length; i++)
                {
                    int index = i;
                    existingSlots[i].Initialize(index);
                    existingSlots[i].OnSlotClicked += HandleSlotClicked;
                    _slotViews.Add(existingSlots[i]);
                }
                return;
            }

            // 2. Otherwise, generate standard slots
            int targetCount = _boundInventory != null ? _boundInventory.SlotCount : 24;

            while (_slotViews.Count < targetCount)
            {
                int index = _slotViews.Count;
                GameObject slotObj = CreateSlotObject(slotGridContainer, index);
                InventorySlotUI slotUI = slotObj.GetComponent<InventorySlotUI>();
                slotUI.Initialize(index);
                slotUI.OnSlotClicked += HandleSlotClicked;
                _slotViews.Add(slotUI);
            }
        }

        private void HandleSlotClicked(int slotIndex)
        {
            _selectedSlotIndex = slotIndex;

            for (int i = 0; i < _slotViews.Count; i++)
            {
                _slotViews[i].SetSelected(i == _selectedSlotIndex);
            }

            if (_boundInventory != null && slotIndex >= 0 && slotIndex < _boundInventory.Slots.Count)
            {
                InventorySlot slot = _boundInventory.Slots[slotIndex];
                if (detailsPanel != null)
                {
                    detailsPanel.DisplayItem(slot, slotIndex);
                }
            }
        }

        private void SetupCloseButton()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(() => Close());
            }
        }

        private GameObject CreateSlotObject(Transform parent, int index)
        {
            GameObject slotObj = new GameObject($"Slot_{index:D2}");
            slotObj.transform.SetParent(parent, false);

            RectTransform rect = slotObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(74f, 74f);

            Image bg = slotObj.AddComponent<Image>();
            bg.sprite = UIStyleHelper.SlotSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.10f, 0.13f, 0.16f, 0.95f);

            // Selection Border
            GameObject borderObj = new GameObject("SelectionBorder");
            borderObj.transform.SetParent(slotObj.transform, false);
            RectTransform borderRect = borderObj.AddComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = new Vector2(-2, -2);
            borderRect.offsetMax = new Vector2(2, 2);
            Image borderImg = borderObj.AddComponent<Image>();
            borderImg.sprite = UIStyleHelper.SlotSelectedSprite;
            borderImg.type = Image.Type.Sliced;
            borderObj.SetActive(false);

            // Item Icon
            GameObject iconObj = new GameObject("ItemIcon");
            iconObj.transform.SetParent(slotObj.transform, false);
            RectTransform iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.12f, 0.12f);
            iconRect.anchorMax = new Vector2(0.88f, 0.88f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.preserveAspect = true;
            iconObj.SetActive(false);

            // Quantity Text
            GameObject textObj = new GameObject("QuantityText");
            textObj.transform.SetParent(slotObj.transform, false);
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 0.40f);
            textRect.offsetMin = new Vector2(2f, 2f);
            textRect.offsetMax = new Vector2(-4f, 0f);
            TMP_Text tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 14;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.BottomRight;
            tmp.color = new Color(0.98f, 0.94f, 0.85f, 1f);
            textObj.SetActive(false);

            slotObj.AddComponent<InventorySlotUI>();
            return slotObj;
        }

        private void EnsureComponents()
        {
            if (windowRoot == null) windowRoot = gameObject;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null && windowRoot != null) canvasGroup = windowRoot.GetComponent<CanvasGroup>();
            if (canvasGroup == null && windowRoot != null) canvasGroup = windowRoot.AddComponent<CanvasGroup>();
            if (windowPanel == null && windowRoot != null) windowPanel = windowRoot.GetComponent<RectTransform>();

            if (detailsPanel == null) detailsPanel = GetComponentInChildren<InventoryItemDetailsUI>(true);
            if (closeButton == null)
            {
                Transform btn = transform.Find("CloseButton") ?? transform.Find("Close_Button") ?? transform.Find("Btn_Close");
                if (btn != null) closeButton = btn.GetComponent<Button>();
            }
        }

        public void Configure(
            GameObject root,
            CanvasGroup group,
            RectTransform panel,
            TMP_Text woodText,
            TMP_Text goldText,
            Transform gridContainer,
            InventoryItemDetailsUI details,
            Button closeBtn)
        {
            windowRoot = root;
            canvasGroup = group;
            windowPanel = panel;
            woodCounterText = woodText;
            goldCounterText = goldText;
            slotGridContainer = gridContainer;
            detailsPanel = details;
            closeButton = closeBtn;

            EnsureComponents();
            SetupCloseButton();
        }

        /// <summary>
        /// Programmatically builds the clean Universal Inventory Window hierarchy under the given Canvas.
        /// </summary>
        public static UniversalInventoryUI BuildRuntimeCanvasHierarchy(GameObject canvasGo)
        {
            if (canvasGo == null) return null;

            UniversalInventoryUI existing = canvasGo.GetComponentInChildren<UniversalInventoryUI>(true);
            if (existing != null) return existing;

            // 1. Root Window Panel
            GameObject windowObj = new GameObject("Inventory_Window");
            windowObj.transform.SetParent(canvasGo.transform, false);

            RectTransform windowRect = windowObj.AddComponent<RectTransform>();
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);
            windowRect.anchoredPosition = Vector2.zero;
            windowRect.sizeDelta = new Vector2(960f, 560f);

            Image windowBg = windowObj.AddComponent<Image>();
            windowBg.sprite = UIStyleHelper.PanelSprite;
            windowBg.type = Image.Type.Sliced;
            windowBg.color = new Color(0.08f, 0.10f, 0.13f, 0.96f);

            CanvasGroup canvasGroup = windowObj.AddComponent<CanvasGroup>();
            UniversalInventoryUI uiController = windowObj.AddComponent<UniversalInventoryUI>();

            // 2. Header
            GameObject headerObj = new GameObject("Header");
            headerObj.transform.SetParent(windowObj.transform, false);
            RectTransform headerRect = headerObj.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, 65f);

            // Title
            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(headerObj.transform, false);
            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(0f, 0.5f);
            titleRect.pivot = new Vector2(0f, 0.5f);
            titleRect.anchoredPosition = new Vector2(25f, 0f);
            titleRect.sizeDelta = new Vector2(400f, 40f);
            TMP_Text titleText = titleObj.AddComponent<TextMeshProUGUI>();
            titleText.text = "<b>CHARACTER INVENTORY</b> <size=13><color=#64748b>[TAB]</color></size>";
            titleText.fontSize = 22;
            titleText.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            titleText.alignment = TextAlignmentOptions.MidlineLeft;

            // Wood Balance
            GameObject woodBalObj = new GameObject("WoodBalance");
            woodBalObj.transform.SetParent(headerObj.transform, false);
            RectTransform woodBalRect = woodBalObj.AddComponent<RectTransform>();
            woodBalRect.anchorMin = new Vector2(1f, 0.5f);
            woodBalRect.anchorMax = new Vector2(1f, 0.5f);
            woodBalRect.pivot = new Vector2(1f, 0.5f);
            woodBalRect.anchoredPosition = new Vector2(-160f, 0f);
            woodBalRect.sizeDelta = new Vector2(140f, 35f);
            TMP_Text woodBalText = woodBalObj.AddComponent<TextMeshProUGUI>();
            woodBalText.text = "Wood: <b>0</b>";
            woodBalText.fontSize = 17;
            woodBalText.color = new Color(0.96f, 0.78f, 0.35f, 1f);
            woodBalText.alignment = TextAlignmentOptions.MidlineRight;

            // Gold Balance
            GameObject goldBalObj = new GameObject("GoldBalance");
            goldBalObj.transform.SetParent(headerObj.transform, false);
            RectTransform goldBalRect = goldBalObj.AddComponent<RectTransform>();
            goldBalRect.anchorMin = new Vector2(1f, 0.5f);
            goldBalRect.anchorMax = new Vector2(1f, 0.5f);
            goldBalRect.pivot = new Vector2(1f, 0.5f);
            goldBalRect.anchoredPosition = new Vector2(-60f, 0f);
            goldBalRect.sizeDelta = new Vector2(90f, 35f);
            TMP_Text goldBalText = goldBalObj.AddComponent<TextMeshProUGUI>();
            goldBalText.text = "Gold: <b>0</b>";
            goldBalText.fontSize = 17;
            goldBalText.color = new Color(0.98f, 0.85f, 0.25f, 1f);
            goldBalText.alignment = TextAlignmentOptions.MidlineRight;

            // Close Button
            GameObject closeBtnObj = new GameObject("CloseButton");
            closeBtnObj.transform.SetParent(headerObj.transform, false);
            RectTransform closeRect = closeBtnObj.AddComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.anchoredPosition = new Vector2(-18f, 0f);
            closeRect.sizeDelta = new Vector2(36f, 36f);

            Image closeImg = closeBtnObj.AddComponent<Image>();
            closeImg.sprite = UIStyleHelper.ButtonSprite;
            closeImg.type = Image.Type.Sliced;
            closeImg.color = new Color(0.35f, 0.15f, 0.15f, 1f);
            Button closeBtn = closeBtnObj.AddComponent<Button>();

            GameObject closeTextObj = new GameObject("Label");
            closeTextObj.transform.SetParent(closeBtnObj.transform, false);
            RectTransform closeTextRect = closeTextObj.AddComponent<RectTransform>();
            closeTextRect.anchorMin = Vector2.zero;
            closeTextRect.anchorMax = Vector2.one;
            closeTextRect.offsetMin = Vector2.zero;
            closeTextRect.offsetMax = Vector2.zero;
            TMP_Text closeText = closeTextObj.AddComponent<TextMeshProUGUI>();
            closeText.text = "X";
            closeText.fontSize = 18;
            closeText.fontStyle = FontStyles.Bold;
            closeText.alignment = TextAlignmentOptions.Center;
            closeText.color = Color.white;

            // 3. Content Panel: Inventory Grid (Left) & Details (Right)
            GameObject contentObj = new GameObject("Content_Panel");
            contentObj.transform.SetParent(windowObj.transform, false);
            RectTransform contentRect = contentObj.AddComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(25f, 25f);
            contentRect.offsetMax = new Vector2(-25f, -75f);

            // Left: Grid
            GameObject gridObj = new GameObject("Slot_Grid");
            gridObj.transform.SetParent(contentObj.transform, false);
            RectTransform gridRect = gridObj.AddComponent<RectTransform>();
            gridRect.anchorMin = new Vector2(0f, 0f);
            gridRect.anchorMax = new Vector2(0f, 1f);
            gridRect.pivot = new Vector2(0f, 0.5f);
            gridRect.anchoredPosition = Vector2.zero;
            gridRect.sizeDelta = new Vector2(520f, 0f);

            GridLayoutGroup grid = gridObj.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(74f, 74f);
            grid.spacing = new Vector2(10f, 10f);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 6;

            // Right: Details Panel
            GameObject detailsObj = new GameObject("Details_Panel");
            detailsObj.transform.SetParent(contentObj.transform, false);
            RectTransform detailsRect = detailsObj.AddComponent<RectTransform>();
            detailsRect.anchorMin = new Vector2(0f, 0f);
            detailsRect.anchorMax = new Vector2(1f, 1f);
            detailsRect.offsetMin = new Vector2(545f, 0f);
            detailsRect.offsetMax = Vector2.zero;

            Image detailsBg = detailsObj.AddComponent<Image>();
            detailsBg.sprite = UIStyleHelper.PanelSprite;
            detailsBg.type = Image.Type.Sliced;
            detailsBg.color = new Color(0.12f, 0.15f, 0.19f, 0.95f);

            InventoryItemDetailsUI detailsUI = detailsObj.AddComponent<InventoryItemDetailsUI>();

            // Details Icon
            GameObject detailIconObj = new GameObject("ItemIcon");
            detailIconObj.transform.SetParent(detailsObj.transform, false);
            RectTransform detailIconRect = detailIconObj.AddComponent<RectTransform>();
            detailIconRect.anchorMin = new Vector2(0.5f, 1f);
            detailIconRect.anchorMax = new Vector2(0.5f, 1f);
            detailIconRect.pivot = new Vector2(0.5f, 1f);
            detailIconRect.anchoredPosition = new Vector2(0f, -40f);
            detailIconRect.sizeDelta = new Vector2(96f, 96f);
            Image detailIconImg = detailIconObj.AddComponent<Image>();
            detailIconImg.preserveAspect = true;

            // Details Name
            GameObject detailNameObj = new GameObject("ItemName");
            detailNameObj.transform.SetParent(detailsObj.transform, false);
            RectTransform detailNameRect = detailNameObj.AddComponent<RectTransform>();
            detailNameRect.anchorMin = new Vector2(0.5f, 1f);
            detailNameRect.anchorMax = new Vector2(0.5f, 1f);
            detailNameRect.pivot = new Vector2(0.5f, 1f);
            detailNameRect.anchoredPosition = new Vector2(0f, -155f);
            detailNameRect.sizeDelta = new Vector2(320f, 32f);
            TMP_Text detailNameText = detailNameObj.AddComponent<TextMeshProUGUI>();
            detailNameText.text = "Select an Item";
            detailNameText.fontSize = 22;
            detailNameText.fontStyle = FontStyles.Bold;
            detailNameText.alignment = TextAlignmentOptions.Center;
            detailNameText.color = Color.white;

            // Details Category Tag
            GameObject detailCatObj = new GameObject("Category");
            detailCatObj.transform.SetParent(detailsObj.transform, false);
            RectTransform detailCatRect = detailCatObj.AddComponent<RectTransform>();
            detailCatRect.anchorMin = new Vector2(0.5f, 1f);
            detailCatRect.anchorMax = new Vector2(0.5f, 1f);
            detailCatRect.pivot = new Vector2(0.5f, 1f);
            detailCatRect.anchoredPosition = new Vector2(0f, -195f);
            detailCatRect.sizeDelta = new Vector2(320f, 24f);
            TMP_Text detailCatText = detailCatObj.AddComponent<TextMeshProUGUI>();
            detailCatText.fontSize = 14;
            detailCatText.color = new Color(0.38f, 0.72f, 0.95f, 1f);
            detailCatText.alignment = TextAlignmentOptions.Center;

            // Details Quantity
            GameObject detailQtyObj = new GameObject("Quantity");
            detailQtyObj.transform.SetParent(detailsObj.transform, false);
            RectTransform detailQtyRect = detailQtyObj.AddComponent<RectTransform>();
            detailQtyRect.anchorMin = new Vector2(0.5f, 1f);
            detailQtyRect.anchorMax = new Vector2(0.5f, 1f);
            detailQtyRect.pivot = new Vector2(0.5f, 1f);
            detailQtyRect.anchoredPosition = new Vector2(0f, -235f);
            detailQtyRect.sizeDelta = new Vector2(320f, 30f);
            TMP_Text detailQtyText = detailQtyObj.AddComponent<TextMeshProUGUI>();
            detailQtyText.fontSize = 16;
            detailQtyText.color = new Color(0.96f, 0.78f, 0.35f, 1f);
            detailQtyText.alignment = TextAlignmentOptions.Center;

            // Details Use Button
            GameObject detailUseObj = new GameObject("UseButton");
            detailUseObj.transform.SetParent(detailsObj.transform, false);
            RectTransform detailUseRect = detailUseObj.AddComponent<RectTransform>();
            detailUseRect.anchorMin = new Vector2(0.5f, 0f);
            detailUseRect.anchorMax = new Vector2(0.5f, 0f);
            detailUseRect.pivot = new Vector2(0.5f, 0f);
            detailUseRect.anchoredPosition = new Vector2(0f, 35f);
            detailUseRect.sizeDelta = new Vector2(260f, 48f);
            Image detailUseImg = detailUseObj.AddComponent<Image>();
            detailUseImg.sprite = UIStyleHelper.ButtonSprite;
            detailUseImg.type = Image.Type.Sliced;
            detailUseImg.color = new Color(0.18f, 0.55f, 0.35f, 1f);
            Button detailUseBtn = detailUseObj.AddComponent<Button>();

            GameObject detailUseLabelObj = new GameObject("Label");
            detailUseLabelObj.transform.SetParent(detailUseObj.transform, false);
            RectTransform detailUseLabelRect = detailUseLabelObj.AddComponent<RectTransform>();
            detailUseLabelRect.anchorMin = Vector2.zero;
            detailUseLabelRect.anchorMax = Vector2.one;
            detailUseLabelRect.offsetMin = Vector2.zero;
            detailUseLabelRect.offsetMax = Vector2.zero;
            TMP_Text detailUseLabel = detailUseLabelObj.AddComponent<TextMeshProUGUI>();
            detailUseLabel.text = "Use Item";
            detailUseLabel.fontSize = 16;
            detailUseLabel.fontStyle = FontStyles.Bold;
            detailUseLabel.alignment = TextAlignmentOptions.Center;
            detailUseLabel.color = Color.white;

            // Wire Details UI references
            detailsUI.Configure(detailIconImg, detailNameText, detailCatText, detailQtyText, detailUseBtn, detailUseLabel);

            // Wire all references cleanly
            uiController.Configure(
                windowObj,
                canvasGroup,
                windowRect,
                woodBalText,
                goldBalText,
                gridObj.transform,
                detailsUI,
                closeBtn
            );

            return uiController;
        }
    }
}
