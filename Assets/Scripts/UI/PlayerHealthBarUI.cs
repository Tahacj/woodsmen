using UnityEngine;
using UnityEngine.UI;
using Woodsmen.Combat;
using Woodsmen.Inventory;

namespace Woodsmen.UI
{
    /// <summary>
    /// HUD script that manages the player's health bar using an Image with a Filled fill method.
    /// Binds to the local player's CharacterHealth component.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class PlayerHealthBarUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [Tooltip("The Image component set to 'Filled'. Will auto-detect if attached to same GameObject.")]
        [SerializeField] private Image fillImage;

        [Header("Juice (Optional)")]
        [Tooltip("Should the health bar punch/pulse when taking damage?")]
        [SerializeField] private bool punchOnDamage = true;

        private CharacterHealth _boundHealth;

        private void Awake()
        {
            if (fillImage == null)
            {
                fillImage = GetComponent<Image>();
            }
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
            if (_boundHealth == null)
            {
                TryBind();
            }
        }

        private void TryBind()
        {
            if (_boundHealth != null) return;

            // Use PlayerInventory.LocalPlayerInstance as a reliable way to find the local player object
            if (PlayerInventory.LocalPlayerInstance != null)
            {
                _boundHealth = PlayerInventory.LocalPlayerInstance.GetComponent<CharacterHealth>();
                if (_boundHealth != null)
                {
                    _boundHealth.OnHealthChanged += HandleHealthChanged;
                    UpdateDisplay(_boundHealth.CurrentHealth, _boundHealth.MaxHealth, false);
                }
            }
        }

        private void Unbind()
        {
            if (_boundHealth != null)
            {
                _boundHealth.OnHealthChanged -= HandleHealthChanged;
                _boundHealth = null;
            }
        }

        private void HandleHealthChanged(float currentHealth, float maxHealth)
        {
            bool tookDamage = false;
            if (fillImage != null)
            {
                // If the new fill amount is less than the current, it's damage
                float newFill = maxHealth > 0 ? currentHealth / maxHealth : 0f;
                if (newFill < fillImage.fillAmount)
                {
                    tookDamage = true;
                }
            }
            
            UpdateDisplay(currentHealth, maxHealth, punchOnDamage && tookDamage);
        }

        private void UpdateDisplay(float currentHealth, float maxHealth, bool animate)
        {
            if (fillImage != null)
            {
                fillImage.fillAmount = maxHealth > 0 ? currentHealth / maxHealth : 0f;
            }

            if (animate)
            {
#if PRIMETWEEN_INSTALLED || UNITY_EDITOR
                PrimeTween.Tween.PunchScale(transform, new Vector3(0.15f, 0.15f, 0f), duration: 0.2f);
#endif
            }
        }
    }
}
