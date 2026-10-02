using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Woodsmen.Inventory.UI
{
    /// <summary>
    /// Item Details inspector panel displayed alongside the inventory grid.
    /// Shows detailed lore, category, stack size, and handles:
    /// - Polymorphic item usage (consuming potions, etc.)
    /// - Dropping universal items (CharacterClass.Both) with customizable quantity slider (defaults to 50% owned)
    /// </summary>
    public class InventoryItemDetailsUI : MonoBehaviour
    {
        [Header("UI Element References")]
        [SerializeField] private Image itemIcon;
        [SerializeField] private TMP_Text itemNameText;
        [SerializeField] private TMP_Text categoryText;
        [SerializeField] private TMP_Text quantityText;
        [SerializeField] private Button useButton;
        [SerializeField] private TMP_Text useButtonLabel;
        [SerializeField] private GameObject emptyHintObject;

        [Header("Drop Controls")]
        [SerializeField] private Button dropButton;
        [SerializeField] private Slider dropAmountSlider;
        [SerializeField] private TMP_Text dropAmountText;

        private int _currentSlotIndex = -1;
        private int _currentTotalQuantity = 0;
        private PlayerInventory _boundInventory;

        private void Awake()
        {
            EnsureReferences();
        }

        private void Start()
        {
            Clear();
        }

        /// <summary>
        /// Defensively auto-locates UI components if they were not assigned in the Inspector.
        /// Matches the hierarchy created in the scene: DropButton, Drop Amount Slider, drop/total text.
        /// </summary>
        public void EnsureReferences()
        {
            if (itemIcon == null) itemIcon = transform.Find("ItemIcon")?.GetComponent<Image>();
            if (itemNameText == null) itemNameText = transform.Find("ItemName")?.GetComponent<TMP_Text>();
            if (categoryText == null) categoryText = transform.Find("Category")?.GetComponent<TMP_Text>();
            if (quantityText == null) quantityText = transform.Find("Quantity")?.GetComponent<TMP_Text>();
            if (useButton == null) useButton = transform.Find("UseButton")?.GetComponent<Button>();
            if (useButtonLabel == null && useButton != null) useButtonLabel = useButton.GetComponentInChildren<TMP_Text>();

            if (dropButton == null)
            {
                Transform db = transform.Find("DropButton") ?? transform.Find("Drop_Button") ?? transform.Find("Btn_Drop");
                if (db != null) dropButton = db.GetComponent<Button>();
            }

            if (dropAmountSlider == null)
            {
                Transform sld = transform.Find("Drop Amount Slider") 
                             ?? transform.Find("DropAmountSlider") 
                             ?? transform.Find("Drop_Slider") 
                             ?? transform.Find("Drop Amount");
                if (sld != null) dropAmountSlider = sld.GetComponent<Slider>();
            }

            if (dropAmountText == null)
            {
                // Check directly under Drop Amount Slider or within Details_Panel
                if (dropAmountSlider != null)
                {
                    Transform txt = dropAmountSlider.transform.Find("drop/total text") 
                                 ?? dropAmountSlider.transform.Find("drop_total_text") 
                                 ?? dropAmountSlider.transform.Find("drop/total")
                                 ?? dropAmountSlider.transform.Find("Text");
                    if (txt != null) dropAmountText = txt.GetComponent<TMP_Text>();

                    if (dropAmountText == null)
                    {
                        foreach (Transform child in dropAmountSlider.transform)
                        {
                            if (child.name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                child.name.IndexOf("total", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                dropAmountText = child.GetComponent<TMP_Text>();
                                if (dropAmountText != null) break;
                            }
                        }
                    }
                }

                if (dropAmountText == null)
                {
                    Transform txt = transform.Find("drop/total text") ?? transform.Find("drop_total_text");
                    if (txt != null) dropAmountText = txt.GetComponent<TMP_Text>();
                }
            }
        }

        public void Configure(
            Image icon,
            TMP_Text nameText,
            TMP_Text catText,
            TMP_Text qtyText,
            Button button,
            TMP_Text buttonLabel,
            Button dropBtn = null,
            Slider dropSlider = null,
            TMP_Text dropText = null)
        {
            itemIcon = icon;
            itemNameText = nameText;
            categoryText = catText;
            quantityText = qtyText;
            useButton = button;
            useButtonLabel = buttonLabel;
            if (dropBtn != null) dropButton = dropBtn;
            if (dropSlider != null) dropAmountSlider = dropSlider;
            if (dropText != null) dropAmountText = dropText;
        }

        public void Bind(PlayerInventory inventory)
        {
            _boundInventory = inventory;
            EnsureReferences();

            if (useButton != null)
            {
                useButton.onClick.RemoveAllListeners();
                useButton.onClick.AddListener(HandleUseClicked);
            }

            if (dropButton != null)
            {
                dropButton.onClick.RemoveAllListeners();
                dropButton.onClick.AddListener(HandleDropClicked);
            }

            if (dropAmountSlider != null)
            {
                dropAmountSlider.onValueChanged.RemoveAllListeners();
                dropAmountSlider.onValueChanged.AddListener(HandleSliderValueChanged);
            }
        }

        public void DisplayItem(InventorySlot slot, int slotIndex)
        {
            EnsureReferences();
            _currentSlotIndex = slotIndex;

            if (slot.IsEmpty || slot.Item == null)
            {
                Clear();
                return;
            }

            IInventoryItem item = slot.Item;

            // Total quantity of this item across the entire inventory
            int totalInInventory = _boundInventory != null ? _boundInventory.GetItemCount(item.Id) : slot.quantity;
            if (totalInInventory <= 0) totalInInventory = slot.quantity;
            _currentTotalQuantity = totalInInventory;

            if (emptyHintObject != null) emptyHintObject.SetActive(false);

            if (itemIcon != null)
            {
                itemIcon.gameObject.SetActive(true);
                itemIcon.sprite = item.Icon != null ? item.Icon : UIStyleHelper.GetItemIcon(item.Id);
                itemIcon.color = Color.white;
            }

            if (itemNameText != null)
            {
                itemNameText.text = item.DisplayName;
            }

            if (categoryText != null)
            {
                string classTag = item.AllowedClass switch
                {
                    CharacterClass.Warrior => " <color=#f87171>[Warrior Only]</color>",
                    CharacterClass.Lumberjack => " <color=#34d399>[Lumberjack Only]</color>",
                    _ => " <color=#94a3b8>[All Classes]</color>"
                };
                categoryText.text = $"[{item.Category}]{classTag}";
            }

            if (quantityText != null)
            {
                quantityText.text = $"Owned: <b>{totalInInventory}</b>";
            }

            // 1. Polymorphic usage and class validation
            SetupUseButton(item);

            // 2. Droppable mechanics: items for both players (Universal / CharacterClass.Both) are droppable
            SetupDropControls(item, totalInInventory);
        }

        private void SetupUseButton(IInventoryItem item)
        {
            if (useButton == null) return;

            GameObject user = _boundInventory != null ? _boundInventory.gameObject : null;
            bool isUsable = item.IsUsable;
            bool isClassAllowed = user == null || item.AllowedClass.IsClassCompatible(user.GetCharacterClass());
            bool canUse = item.CanUse(user);

            if (!isUsable)
            {
                useButton.gameObject.SetActive(false);
            }
            else
            {
                useButton.gameObject.SetActive(true);

                if (!isClassAllowed)
                {
                    useButton.interactable = false;
                    if (useButtonLabel != null)
                    {
                        useButtonLabel.text = item.AllowedClass == CharacterClass.Warrior
                            ? "Requires Warrior"
                            : "Requires Lumberjack";
                    }
                }
                else
                {
                    useButton.interactable = canUse;
                    if (useButtonLabel != null)
                    {
                        if (item.Category == ItemCategory.Consumable)
                        {
                            useButtonLabel.text = canUse ? "Consume Item" : "Health Full";
                        }
                        else
                        {
                            useButtonLabel.text = "Use Item";
                        }
                    }
                }
            }
        }

        private void SetupDropControls(IInventoryItem item, int totalQuantity)
        {
            // Strictly enforced: Weapons, Attributes, and Physical Upgrades are not droppable.
            // Only consumables (and universal resources) designated for Both classes can be dropped.
            bool isDroppable = item.IsDroppable;

            if (!isDroppable || totalQuantity <= 0)
            {
                if (dropButton != null) dropButton.gameObject.SetActive(false);
                if (dropAmountSlider != null) dropAmountSlider.gameObject.SetActive(false);
                return;
            }

            if (dropButton != null)
            {
                dropButton.gameObject.SetActive(true);
                dropButton.interactable = true;
            }

            if (dropAmountSlider != null)
            {
                dropAmountSlider.gameObject.SetActive(true);

                // Default amount to drop: always half of the total owned quantity
                // e.g. 50 -> 25, 61 -> 31, 1 -> 1
                int defaultDrop = Mathf.Clamp(Mathf.CeilToInt(totalQuantity / 2f), 1, totalQuantity);

                dropAmountSlider.onValueChanged.RemoveListener(HandleSliderValueChanged);
                dropAmountSlider.minValue = 1;
                dropAmountSlider.maxValue = totalQuantity;
                dropAmountSlider.wholeNumbers = true;
                dropAmountSlider.value = defaultDrop;
                dropAmountSlider.interactable = totalQuantity > 1;
                dropAmountSlider.onValueChanged.AddListener(HandleSliderValueChanged);

                UpdateDropText(defaultDrop, totalQuantity);
            }
        }

        private void UpdateDropText(int dropAmount, int totalQuantity)
        {
            EnsureReferences();
            if (dropAmountText != null)
            {
                dropAmountText.text = $"{dropAmount}/{totalQuantity}";
            }
        }

        private void HandleSliderValueChanged(float value)
        {
            int amount = Mathf.RoundToInt(value);
            UpdateDropText(amount, _currentTotalQuantity);
        }

        public void Clear()
        {
            _currentSlotIndex = -1;
            _currentTotalQuantity = 0;

            EnsureReferences();

            if (emptyHintObject != null) emptyHintObject.SetActive(true);

            if (itemIcon != null) itemIcon.gameObject.SetActive(false);
            if (itemNameText != null) itemNameText.text = "Select an Item";
            if (categoryText != null) categoryText.text = string.Empty;
            if (quantityText != null) quantityText.text = string.Empty;
            if (useButton != null) useButton.gameObject.SetActive(false);
            if (dropButton != null) dropButton.gameObject.SetActive(false);
            if (dropAmountSlider != null) dropAmountSlider.gameObject.SetActive(false);
        }

        private void HandleUseClicked()
        {
            if (_boundInventory != null && _currentSlotIndex >= 0)
            {
                _boundInventory.UseItem(_currentSlotIndex);
            }
        }

        private void HandleDropClicked()
        {
            if (_boundInventory == null || _currentSlotIndex < 0) return;
            if (_currentSlotIndex >= _boundInventory.Slots.Count) return;

            InventorySlot currentSlot = _boundInventory.Slots[_currentSlotIndex];
            if (currentSlot.IsEmpty || currentSlot.Item == null) return;

            int amountToDrop = 1;
            if (dropAmountSlider != null)
            {
                amountToDrop = Mathf.Clamp(Mathf.RoundToInt(dropAmountSlider.value), 1, _currentTotalQuantity);
            }

            bool success = _boundInventory.DropItem(_currentSlotIndex, amountToDrop);
            if (success)
            {
                int refreshIndex = _currentSlotIndex;
                // Guard: slot count may have changed after the drop (SyncList update or slot cleared)
                if (refreshIndex >= 0 && refreshIndex < _boundInventory.Slots.Count)
                {
                    InventorySlot updatedSlot = _boundInventory.Slots[refreshIndex];
                    if (!updatedSlot.IsEmpty && updatedSlot.Item != null)
                    {
                        DisplayItem(updatedSlot, refreshIndex);
                    }
                    else
                    {
                        Clear();
                    }
                }
                else
                {
                    Clear();
                }
            }
        }
    }
}
