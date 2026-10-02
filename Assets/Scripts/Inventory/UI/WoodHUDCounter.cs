using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Woodsmen.Inventory.UI
{
    /// <summary>
    /// Lightweight HUD text display for collected wood.
    /// Listens to IReadOnlyInventory.OnWoodChanged with zero polling allocations.
    /// Compatible with both TextMeshPro (TMP_Text) and standard Unity UI Text.
    /// </summary>
    public class WoodHUDCounter : MonoBehaviour
    {
        [Header("Text Component (Auto-detected if unassigned)")]
        [Tooltip("Assign TextMeshProUGUI or standard Text. If empty, will look on this GameObject.")]
        [SerializeField] private TMP_Text tmpText;
        [SerializeField] private Text legacyText;

        [Header("Formatting")]
        [SerializeField] private string labelPrefix = "Wood: ";

        [Header("Juice / Feedback")]
        [SerializeField] private bool punchOnCollect = true;

        private IReadOnlyInventory _boundInventory;

        private void Awake()
        {
            if (tmpText == null) tmpText = GetComponent<TMP_Text>();
            if (legacyText == null) legacyText = GetComponent<Text>();
        }

        private void OnEnable()
        {
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
                _boundInventory.OnWoodChanged += HandleWoodChanged;
                _lastDisplayedAmount = _boundInventory.Wood;
                UpdateDisplay(_lastDisplayedAmount, animate: false);
            }
        }

        private void Unbind()
        {
            if (_boundInventory != null)
            {
                _boundInventory.OnWoodChanged -= HandleWoodChanged;
                _boundInventory = null;
            }
        }

        private void HandleWoodChanged(int currentWood)
        {
            if (currentWood == _lastDisplayedAmount) return;
            _lastDisplayedAmount = currentWood;
            UpdateDisplay(currentWood, animate: punchOnCollect);
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
                PrimeTween.Tween.PunchScale(transform, new Vector3(0.18f, 0.18f, 0f), duration: 0.22f);
            }
        }
    }
}
