using System;
using UnityEngine;
using UnityEngine.UI;

namespace Woodsmen.Shop.UI
{
    /// <summary>
    /// UI representation of a single item slot in the shop's item list (Slot_Grid).
    /// Displays item icon, border frame, selection state, and handles click events.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopItemSlotUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [Tooltip("Image component displaying the item icon.")]
        [SerializeField] private Image iconImage;

        [Tooltip("Image component displaying the slot border frame.")]
        [SerializeField] private Image borderImage;

        [Tooltip("Button component handling selection.")]
        [SerializeField] private Button button;

        [Header("Selection Colors")]
        [SerializeField] private Color selectedBorderColor = new Color(1f, 0.85f, 0.2f, 1f); // Gold
        [SerializeField] private Color normalBorderColor = new Color(0.7f, 0.65f, 0.6f, 0.9f); // Subtle bronze

        private ShopItemDefinition _item;
        private Action<ShopItemDefinition> _onClicked;

        private void Awake()
        {
            if (iconImage == null) iconImage = GetComponent<Image>();

            if (borderImage == null)
            {
                var borderTrans = transform.Find("border") ?? transform.Find("Border");
                if (borderTrans != null) borderImage = borderTrans.GetComponent<Image>();
            }

            if (button == null)
            {
                button = GetComponent<Button>() ?? gameObject.AddComponent<Button>();
            }

            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
                button.onClick.AddListener(HandleClick);
            }
        }

        public void Bind(ShopItemDefinition item, bool isSelected, Action<ShopItemDefinition> onClicked)
        {
            _item = item;
            _onClicked = onClicked;

            if (iconImage != null)
            {
                if (item != null && item.Icon != null)
                {
                    iconImage.sprite = item.Icon;
                    iconImage.color = Color.white;
                    iconImage.enabled = true;
                }
                else
                {
                    iconImage.enabled = false;
                }
            }

            SetSelected(isSelected);
        }

        public void SetSelected(bool selected)
        {
            if (borderImage != null)
            {
                borderImage.color = selected ? selectedBorderColor : normalBorderColor;
            }

            transform.localScale = selected ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one;
        }

        private void HandleClick()
        {
            if (_item != null)
            {
                _onClicked?.Invoke(_item);
            }
        }
    }
}
