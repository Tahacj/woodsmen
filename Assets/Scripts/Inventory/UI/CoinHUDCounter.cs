using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Woodsmen.Inventory.UI
{
    /// <summary>
    /// Lightweight HUD text display for collected coins/money.
    /// Listens to IReadOnlyInventory.OnMoneyChanged with zero polling allocations.
    /// Automatically detects TMP_Text or Text on this GameObject or in its children.
    /// </summary>
    public class CoinHUDCounter : MonoBehaviour
    {
        [Header("Text Component (Auto-detected if unassigned)")]
        [Tooltip("Assign TextMeshProUGUI or standard Text. If empty, will look on this GameObject or in its children.")]
        [SerializeField] private TMP_Text tmpText;
        [SerializeField] private Text legacyText;

        [Header("Formatting")]
        [SerializeField] private string labelPrefix = "";

        [Header("Juice / Feedback")]
        [SerializeField] private bool punchOnCollect = true;

        private IReadOnlyInventory _boundInventory;

        private void Awake()
        {
            AutoDetectText();
        }

        private void AutoDetectText()
        {
            if (tmpText == null) tmpText = GetComponent<TMP_Text>() ?? GetComponentInChildren<TMP_Text>(true);
            if (legacyText == null) legacyText = GetComponent<Text>() ?? GetComponentInChildren<Text>(true);
        }

        private void OnEnable()
        {
            AutoDetectText();
            TryBind();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void Update()
        {
            // Defensive auto-bind if the player spawns asynchronously after HUD starts
            if (_boundInventory == null)
            {
                TryBind();
            }
        }

        private int _lastDisplayedAmount = -1;

        private void TryBind()
        {
            if (_boundInventory != null) return;

            if (PlayerInventory.LocalPlayerInstance != null)
            {
                _boundInventory = PlayerInventory.LocalPlayerInstance;
                _boundInventory.OnMoneyChanged += HandleMoneyChanged;
                _lastDisplayedAmount = _boundInventory.Money;
                UpdateDisplay(_lastDisplayedAmount, animate: false);
            }
        }

        private void Unbind()
        {
            if (_boundInventory != null)
            {
                _boundInventory.OnMoneyChanged -= HandleMoneyChanged;
                _boundInventory = null;
            }
        }

        private void HandleMoneyChanged(int currentMoney)
        {
            if (currentMoney == _lastDisplayedAmount) return;
            _lastDisplayedAmount = currentMoney;
            UpdateDisplay(currentMoney, animate: punchOnCollect);
        }

        private void UpdateDisplay(int amount, bool animate)
        {
            string displayText = $"{labelPrefix}{amount}";

            if (tmpText != null)
            {
                tmpText.text = displayText;
            }
            if (legacyText != null)
            {
                legacyText.text = displayText;
            }

            if (animate)
            {
                PrimeTween.Tween.PunchScale(transform, new Vector3(0.2f, 0.2f, 0f), duration: 0.22f);
            }
        }
    }
}
