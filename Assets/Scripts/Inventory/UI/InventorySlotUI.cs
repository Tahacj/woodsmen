using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Woodsmen.Inventory.UI
{
    /// <summary>
    /// UI Controller for an individual inventory slot item view.
    /// Handles slot rendering, item icons, stack counts, and click/selection events.
    /// </summary>
    public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("UI Element References")]
        [SerializeField] private Image slotBackground;
        [SerializeField] private Image itemIcon;
        [SerializeField] private TMP_Text quantityText;
        [SerializeField] private Image selectionBorder;

        public int SlotIndex { get; private set; } = -1;
        public InventorySlot CurrentSlot { get; private set; }

        public event Action<int> OnSlotClicked;
        public event Action<int> OnSlotHovered;

        private bool _isSelected;

        public void Initialize(int index)
        {
            SlotIndex = index;
            EnsureComponents();
            ClearDisplay();
        }

        private void EnsureComponents()
        {
            if (slotBackground == null) slotBackground = GetComponent<Image>();
            if (selectionBorder == null)
            {
                Transform borderTransform = transform.Find("SelectionBorder");
                if (borderTransform != null) selectionBorder = borderTransform.GetComponent<Image>();
            }
            if (itemIcon == null)
            {
                Transform iconTransform = transform.Find("ItemIcon");
                if (iconTransform != null) itemIcon = iconTransform.GetComponent<Image>();
            }
            if (quantityText == null)
            {
                quantityText = GetComponentInChildren<TMP_Text>();
            }
        }

        public void Bind(int index, InventorySlot slot, bool isSelected, CharacterClass playerClass = CharacterClass.Both)
        {
            SlotIndex = index;
            CurrentSlot = slot;
            _isSelected = isSelected;

            EnsureComponents();

            if (selectionBorder != null)
            {
                selectionBorder.gameObject.SetActive(isSelected);
            }

            if (slot.IsEmpty)
            {
                ClearDisplay();
                return;
            }

            IInventoryItem item = slot.Item;
            if (item != null)
            {
                if (itemIcon != null)
                {
                    itemIcon.gameObject.SetActive(true);
                    itemIcon.sprite = item.Icon != null ? item.Icon : UIStyleHelper.GetItemIcon(item.Id);

                    bool isCompatible = playerClass == CharacterClass.Both || item.AllowedClass.IsClassCompatible(playerClass);
                    itemIcon.color = isCompatible ? Color.white : new Color(0.75f, 0.75f, 0.75f, 0.45f);
                }

                if (quantityText != null)
                {
                    if (slot.quantity > 1)
                    {
                        quantityText.gameObject.SetActive(true);
                        quantityText.text = slot.quantity > 999 ? "999+" : slot.quantity.ToString();
                    }
                    else
                    {
                        quantityText.gameObject.SetActive(false);
                    }
                }
            }
            else
            {
                ClearDisplay();
            }
        }

        public void SetSelected(bool isSelected)
        {
            _isSelected = isSelected;
            if (selectionBorder != null)
            {
                selectionBorder.gameObject.SetActive(isSelected);
            }
        }

        private void ClearDisplay()
        {
            if (itemIcon != null)
            {
                itemIcon.gameObject.SetActive(false);
                itemIcon.sprite = null;
            }

            if (quantityText != null)
            {
                quantityText.gameObject.SetActive(false);
                quantityText.text = string.Empty;
            }

            if (selectionBorder != null)
            {
                selectionBorder.gameObject.SetActive(_isSelected);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            OnSlotClicked?.Invoke(SlotIndex);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            OnSlotHovered?.Invoke(SlotIndex);
            if (slotBackground != null && !_isSelected)
            {
                slotBackground.color = new Color(0.18f, 0.22f, 0.28f, 1f);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (slotBackground != null && !_isSelected)
            {
                slotBackground.color = new Color(0.10f, 0.13f, 0.16f, 0.95f);
            }
        }
    }
}
