using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if PRIMETWEEN_INSTALLED || UNITY_EDITOR
using PrimeTween;
#endif
using Woodsmen.Inventory;
using Woodsmen.Inventory.UI;

namespace Woodsmen.Shop.UI
{
    /// <summary>
    /// Master UI Controller for the Shop Canvas.
    /// Manages the two-panel layout:
    /// - Left: Scrollable/grid list of items for sale (Slot_Grid).
    /// - Right: Item Details Panel with image, name, role badge, description, dual currency pricing (Wood + Coins), and Buy action.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopUI : MonoBehaviour
    {
        public static ShopUI Instance { get; private set; }
        public static bool IsOpen { get; private set; }
        public static IShop CurrentShop { get; private set; }

        [Header("Root Panels & Window")]
        [Tooltip("The main shop window panel GameObject.")]
        [SerializeField] private GameObject shopPanel;

        [Tooltip("Optional CanvasGroup for smooth opening/closing transitions.")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Items List (Left Section)")]
        [Tooltip("Container holding instantiated shop item slots.")]
        [SerializeField] private Transform slotGrid;

        [Tooltip("Prefab or template for an item slot in the grid.")]
        [SerializeField] private GameObject shopItemPrefab;

        [Header("Details Panel (Right Section)")]
        [SerializeField] private GameObject detailsPanel;
        [SerializeField] private Image itemImage;
        [SerializeField] private TMP_Text itemNameText;
        [SerializeField] private TMP_Text roleText;
        [SerializeField] private TMP_Text descriptionText;

        [Header("Price Elements")]
        [SerializeField] private TMP_Text woodPriceText;
        [SerializeField] private Image woodIcon;
        [SerializeField] private TMP_Text goldPriceText;
        [SerializeField] private Image goldIcon;

        [Header("Actions")]
        [SerializeField] private Button buyButton;
        [SerializeField] private TMP_Text buyButtonText;
        [SerializeField] private Button exitButton;

        [Header("Status & Feedback")]
        [SerializeField] private TMP_Text statusFeedbackText;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip purchaseSuccessSound;
        [SerializeField] private AudioClip purchaseFailedSound;

        private IShop _currentShop;
        private PlayerInventory _currentCustomer;
        private ShopItemDefinition _selectedItem;
        private readonly List<ShopItemSlotUI> _activeSlots = new List<ShopItemSlotUI>();
        private readonly List<ShopItemDefinition> _displayedItems = new List<ShopItemDefinition>();
        private int _openedFrame = -1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            var canvas = GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = 25;
            }

            AutoDiscoverReferences();
            SetupListeners();

            // Start closed
            CloseShop(instant: true);

            // Ensure cursor is visible and freely movable at all times
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void Start()
        {
            // Safeguard cursor state for gameplay
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RemoveListeners();
        }

        private void Update()
        {
            if (!IsOpen) return;

            // Frame guard: do not process close input in the exact frame the shop was opened
            if (Time.frameCount <= _openedFrame) return;

            // Close on [Escape] key. (The 'B' key toggle is handled directly by ShopZone)
            if (WasCloseKeyPressed())
            {
                CloseShop();
                return;
            }

            if (_selectedItem != null)
            {
                RefreshAffordability();
            }
        }

        private void RefreshAffordability()
        {
            if (_selectedItem == null) return;

            int buyerWood = _currentCustomer != null ? _currentCustomer.Wood : 0;
            int buyerCoins = _currentCustomer != null ? _currentCustomer.Money : 0;

            bool hasEnoughWood = buyerWood >= _selectedItem.WoodPrice;
            bool hasEnoughCoins = buyerCoins >= _selectedItem.CoinPrice;

            if (woodPriceText != null)
            {
                woodPriceText.color = hasEnoughWood ? Color.white : new Color(1f, 0.35f, 0.35f, 1f);
            }

            if (goldPriceText != null)
            {
                goldPriceText.color = hasEnoughCoins ? Color.white : new Color(1f, 0.35f, 0.35f, 1f);
            }

            UpdateBuyButtonState(hasEnoughWood, hasEnoughCoins);
        }

        private bool WasCloseKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                    return true;
            }
#endif
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                    return true;
            }
            catch { }

            return false;
        }

        private void SetupListeners()
        {
            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(CloseShopButtonClicked);
                exitButton.onClick.AddListener(CloseShopButtonClicked);
            }

            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(HandleBuyClicked);
                buyButton.onClick.AddListener(HandleBuyClicked);
            }
        }

        private void RemoveListeners()
        {
            if (exitButton != null) exitButton.onClick.RemoveListener(CloseShopButtonClicked);
            if (buyButton != null) buyButton.onClick.RemoveListener(HandleBuyClicked);
        }

        [ContextMenu("Auto Discover References")]
        public void AutoDiscoverReferences()
        {
            if (shopPanel == null)
            {
                var panelTrans = transform.Find("Shop Panel");
                if (panelTrans != null) shopPanel = panelTrans.gameObject;
                else shopPanel = gameObject;
            }

            if (canvasGroup == null)
            {
                if (shopPanel != null && !shopPanel.TryGetComponent(out canvasGroup))
                {
                    canvasGroup = shopPanel.AddComponent<CanvasGroup>();
                }
            }

            if (slotGrid == null && shopPanel != null)
            {
                slotGrid = shopPanel.transform.Find("Slot_Grid");
            }

            // Find slot template if unassigned
            if (shopItemPrefab == null && slotGrid != null)
            {
                var template = slotGrid.Find("Shop Item");
                if (template != null)
                {
                    shopItemPrefab = template.gameObject;
                }
            }

            if (detailsPanel == null && shopPanel != null)
            {
                var dp = shopPanel.transform.Find("Details Panel");
                if (dp != null) detailsPanel = dp.gameObject;
            }

            if (detailsPanel != null)
            {
                if (itemImage == null)
                {
                    var imgTrans = detailsPanel.transform.Find("Item's Image");
                    if (imgTrans != null) itemImage = imgTrans.GetComponent<Image>();
                }

                if (itemNameText == null)
                {
                    var nameTrans = detailsPanel.transform.Find("Item Name Text");
                    if (nameTrans != null) itemNameText = nameTrans.GetComponent<TMP_Text>();
                }

                if (roleText == null)
                {
                    var rTrans = detailsPanel.transform.Find("Which Role Text");
                    if (rTrans != null) roleText = rTrans.GetComponent<TMP_Text>();
                }

                if (descriptionText == null)
                {
                    var dTrans = detailsPanel.transform.Find("Item Description Text");
                    if (dTrans != null) descriptionText = dTrans.GetComponent<TMP_Text>();
                }

                if (woodPriceText == null)
                {
                    var wpt = detailsPanel.transform.Find("Price Text/Wood Price/Wood Price Text");
                    if (wpt != null) woodPriceText = wpt.GetComponent<TMP_Text>();
                }

                if (woodIcon == null)
                {
                    var wi = detailsPanel.transform.Find("Price Text/Wood Price/Wood Icon");
                    if (wi != null) woodIcon = wi.GetComponent<Image>();
                }

                if (goldPriceText == null)
                {
                    var gpt = detailsPanel.transform.Find("Price Text/Gold Price/Gold Price Text");
                    if (gpt != null) goldPriceText = gpt.GetComponent<TMP_Text>();
                }

                if (goldIcon == null)
                {
                    var gi = detailsPanel.transform.Find("Price Text/Gold Price/Gold Icon");
                    if (gi != null) goldIcon = gi.GetComponent<Image>();
                }

                if (buyButton == null)
                {
                    var bb = detailsPanel.transform.Find("Buy Button");
                    if (bb != null) buyButton = bb.GetComponent<Button>();
                }

                if (buyButtonText == null && buyButton != null)
                {
                    buyButtonText = buyButton.GetComponentInChildren<TMP_Text>();
                }
            }

            if (exitButton == null && shopPanel != null)
            {
                var eb = shopPanel.transform.Find("Exit Button");
                if (eb != null) exitButton = eb.GetComponent<Button>();
            }

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            }
        }

        #region Open & Close Lifecycle

        /// <summary>
        /// Opens the shop window for a specific customer and shop entity.
        /// </summary>
        public void OpenShop(IShop shop, PlayerInventory customer)
        {
            if (shop == null) return;

            _openedFrame = Time.frameCount;

            _currentShop = shop;
            _currentCustomer = customer != null ? customer : PlayerInventory.LocalPlayerInstance;
            CurrentShop = shop;
            IsOpen = true;

            AutoDiscoverReferences();

            var canvas = GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = 25;
            }

            if (shopPanel != null) shopPanel.SetActive(true);
            gameObject.SetActive(true);

            // Always ensure cursor is visible and freely movable
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            // Animate window entrance
#if PRIMETWEEN_INSTALLED || UNITY_EDITOR
            if (shopPanel != null)
            {
                shopPanel.transform.localScale = Vector3.one * 0.94f;
                Tween.Scale(shopPanel.transform, Vector3.one, 0.2f, Ease.OutBack);
            }
#endif

            PopulateItemList();

            _currentShop.OnShopOpened(_currentCustomer);
            Debug.Log($"<color=#10b981>[ShopUI]</color> Opened shop: {_currentShop.ShopName}");
        }

        /// <summary>
        /// Closes the shop window.
        /// </summary>
        public void CloseShop(bool instant = false)
        {
            IsOpen = false;
            var prevShop = _currentShop;
            _currentShop = null;
            CurrentShop = null;

            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.alpha = 0f;
            }

            if (shopPanel != null && shopPanel != gameObject)
            {
                shopPanel.SetActive(false);
            }

            // Always ensure cursor is visible and freely movable for top-down controls
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (prevShop != null && _currentCustomer != null)
            {
                prevShop.OnShopClosed(_currentCustomer);
            }
        }

        private void CloseShopButtonClicked()
        {
            CloseShop();
        }

        #endregion

        #region Items Grid & Selection

        private void PopulateItemList()
        {
            if (_currentShop == null || slotGrid == null) return;

            // Determine player's character class
            CharacterClass customerClass = _currentCustomer != null 
                ? _currentCustomer.gameObject.GetCharacterClass() 
                : CharacterClass.Both;

            _displayedItems.Clear();
            var shopItems = _currentShop.ItemsForSale;
            if (shopItems != null)
            {
                for (int i = 0; i < shopItems.Count; i++)
                {
                    var item = shopItems[i];
                    if (item == null) continue;

                    // Only show items that accept the player's role (matching class or Both)
                    if (item.IsRoleCompatible(customerClass))
                    {
                        _displayedItems.Add(item);
                    }
                }
            }

            int count = _displayedItems.Count;

            // Ensure slot list capacity
            while (_activeSlots.Count < count)
            {
                GameObject slotGo;
                if (shopItemPrefab != null)
                {
                    slotGo = Instantiate(shopItemPrefab, slotGrid);
                }
                else
                {
                    slotGo = new GameObject("ShopItemSlot", typeof(RectTransform), typeof(Image), typeof(Button));
                    slotGo.transform.SetParent(slotGrid, false);
                }

                slotGo.SetActive(true);

                var slotUI = slotGo.GetComponent<ShopItemSlotUI>();
                if (slotUI == null)
                {
                    slotUI = slotGo.AddComponent<ShopItemSlotUI>();
                }

                _activeSlots.Add(slotUI);
            }

            // Bind each displayed item
            for (int i = 0; i < _activeSlots.Count; i++)
            {
                if (i < count)
                {
                    _activeSlots[i].gameObject.SetActive(true);
                    var item = _displayedItems[i];
                    bool isSelected = _selectedItem == item;
                    _activeSlots[i].Bind(item, isSelected, SelectItem);
                }
                else
                {
                    _activeSlots[i].gameObject.SetActive(false);
                }
            }

            // Select first item by default if none or invalid
            if (count > 0 && (_selectedItem == null || !_displayedItems.Contains(_selectedItem)))
            {
                SelectItem(_displayedItems[0]);
            }
            else if (_selectedItem != null && _displayedItems.Contains(_selectedItem))
            {
                SelectItem(_selectedItem);
            }
            else
            {
                ClearDetailsPanel();
            }
        }

        public void SelectItem(ShopItemDefinition item)
        {
            _selectedItem = item;

            // Update slot highlights using displayed items
            for (int i = 0; i < _activeSlots.Count; i++)
            {
                if (i < _displayedItems.Count)
                {
                    _activeSlots[i].SetSelected(_displayedItems[i] == _selectedItem);
                }
            }

            UpdateDetailsPanel();
        }

        private void UpdateDetailsPanel()
        {
            if (_selectedItem == null || detailsPanel == null)
            {
                ClearDetailsPanel();
                return;
            }

            detailsPanel.SetActive(true);

            // 1. Icon Image
            if (itemImage != null)
            {
                if (_selectedItem.Icon != null)
                {
                    itemImage.sprite = _selectedItem.Icon;
                    itemImage.enabled = true;
                    itemImage.color = Color.white;
                }
                else
                {
                    itemImage.enabled = false;
                }
            }

            // 2. Name
            if (itemNameText != null)
            {
                itemNameText.text = _selectedItem.DisplayName;
            }

            // 3. Role Badge
            if (roleText != null)
            {
                roleText.text = $"Role : {_selectedItem.GetRoleDisplayText()}";
                roleText.color = _selectedItem.GetRoleDisplayColor();
            }

            // 4. Description
            if (descriptionText != null)
            {
                string desc = _selectedItem.Description;
                if (!string.IsNullOrEmpty(_selectedItem.EffectSummary))
                {
                    desc += $"\n<color=#10b981><b>Effect:</b> {_selectedItem.EffectSummary}</color>";
                }
                descriptionText.text = desc;
            }

            // 5. Dual Pricing (Wood + Coins BOTH required)
            int buyerWood = _currentCustomer != null ? _currentCustomer.Wood : 0;
            int buyerCoins = _currentCustomer != null ? _currentCustomer.Money : 0;

            bool hasEnoughWood = buyerWood >= _selectedItem.WoodPrice;
            bool hasEnoughCoins = buyerCoins >= _selectedItem.CoinPrice;

            if (woodPriceText != null)
            {
                woodPriceText.text = _selectedItem.WoodPrice.ToString();
                woodPriceText.color = hasEnoughWood ? Color.white : new Color(1f, 0.35f, 0.35f, 1f); // Red if insufficient
            }

            if (goldPriceText != null)
            {
                goldPriceText.text = _selectedItem.CoinPrice.ToString();
                goldPriceText.color = hasEnoughCoins ? Color.white : new Color(1f, 0.35f, 0.35f, 1f); // Red if insufficient
            }

            // 6. Buy Button State & Validation
            UpdateBuyButtonState(hasEnoughWood, hasEnoughCoins);
        }

        private void UpdateBuyButtonState(bool hasEnoughWood, bool hasEnoughCoins)
        {
            if (buyButton == null) return;

            CharacterClass buyerClass = _currentCustomer != null ? _currentCustomer.gameObject.GetCharacterClass() : CharacterClass.Both;
            bool isAlreadyOwned = ShopZone.IsOneTimePurchaseAlreadyOwned(_currentCustomer, _selectedItem);
            bool isRoleOk = _selectedItem != null && _selectedItem.IsRoleCompatible(buyerClass);
            bool canAfford = hasEnoughWood && hasEnoughCoins;
            bool hasSpace = HasInventorySpace(_currentCustomer, _selectedItem);

            bool canBuy = !isAlreadyOwned && isRoleOk && canAfford && hasSpace;

            buyButton.interactable = canBuy;

            // Keep buy button text as "Buy" - never change to "not enough gold" or other messages
            if (buyButtonText != null)
            {
                buyButtonText.text = "Buy";
                buyButtonText.color = canBuy ? Color.white : new Color(0.75f, 0.75f, 0.75f, 0.5f);
            }

            // Disable / dim the button's graphic color when cannot buy
            if (buyButton.targetGraphic != null)
            {
                buyButton.targetGraphic.color = canBuy ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.55f);
            }
        }

        private bool HasInventorySpace(PlayerInventory buyer, ShopItemDefinition item)
        {
            if (buyer == null || item == null) return false;

            // Attributes and Physical Upgrades do not consume inventory bag slots
            if (item.Category == ShopItemCategory.Attribute || item.Category == ShopItemCategory.PhysicalUpgrade)
            {
                return true;
            }

            string targetItemId = item.Id;
            if (item.Action is Woodsmen.Shop.Actions.GrantInventoryItemAction grantAction && grantAction.InventoryItem != null)
            {
                targetItemId = grantAction.InventoryItem.Id;
            }

            foreach (var slot in buyer.Slots)
            {
                if (slot.IsEmpty) return true;
                if (string.Equals(slot.itemId, targetItemId, StringComparison.OrdinalIgnoreCase))
                {
                    var def = ItemDatabase.GetItem(targetItemId);
                    int maxStack = def != null ? def.MaxStackSize : 1;
                    if (slot.quantity < maxStack) return true;
                }
            }
            return false;
        }

        private void ClearDetailsPanel()
        {
            if (itemImage != null) itemImage.enabled = false;
            if (itemNameText != null) itemNameText.text = "Select an Item";
            if (roleText != null) roleText.text = "";
            if (descriptionText != null) descriptionText.text = "";
            if (woodPriceText != null) woodPriceText.text = "-";
            if (goldPriceText != null) goldPriceText.text = "-";
            if (buyButton != null)
            {
                buyButton.interactable = false;
                if (buyButton.targetGraphic != null)
                {
                    buyButton.targetGraphic.color = new Color(0.55f, 0.55f, 0.55f, 0.55f);
                }
            }
            if (buyButtonText != null)
            {
                buyButtonText.text = "Buy";
                buyButtonText.color = new Color(0.75f, 0.75f, 0.75f, 0.5f);
            }
        }

        #endregion

        #region Purchase Transaction

        private void HandleBuyClicked()
        {
            if (_currentShop == null || _selectedItem == null || _currentCustomer == null) return;

            bool success = _currentShop.PurchaseItem(_currentCustomer, _selectedItem, out string resultMsg);

            if (success)
            {
                PlayAudio(purchaseSuccessSound);
                ShowStatusFeedback($"<color=#10b981>Purchased {_selectedItem.DisplayName}!</color>");

                // Punch animation on buy button
#if PRIMETWEEN_INSTALLED || UNITY_EDITOR
                if (buyButton != null)
                {
                    Tween.PunchScale(buyButton.transform, Vector3.one * 0.15f, 0.22f);
                }
#endif
                // Re-evaluate details and affordable state immediately
                UpdateDetailsPanel();
            }
            else
            {
                PlayAudio(purchaseFailedSound);
                ShowStatusFeedback($"<color=#ef4444>{resultMsg}</color>");
            }
        }

        private void ShowStatusFeedback(string msg)
        {
            if (statusFeedbackText != null)
            {
                statusFeedbackText.text = msg;
                statusFeedbackText.gameObject.SetActive(true);
                CancelInvoke(nameof(HideStatusFeedback));
                Invoke(nameof(HideStatusFeedback), 2.5f);
            }
        }

        private void HideStatusFeedback()
        {
            if (statusFeedbackText != null)
            {
                statusFeedbackText.gameObject.SetActive(false);
            }
        }

        private void PlayAudio(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        #endregion
    }
}
