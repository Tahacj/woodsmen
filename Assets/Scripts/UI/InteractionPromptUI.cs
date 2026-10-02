using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Woodsmen.UI
{
    /// <summary>
    /// Displays world/screen interaction prompts (e.g., "[E] Open Gate", "[E] Close Gate").
    /// Automatically manages the "Interaction Canvas", "Background", and "Text (TMP)".
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionPromptUI : MonoBehaviour
    {
        public static InteractionPromptUI Instance { get; private set; }

        [Header("UI Elements")]
        [Tooltip("Background / container panel of the prompt.")]
        [SerializeField] private GameObject promptContainer;

        [Tooltip("TextMeshPro text displaying the action text.")]
        [SerializeField] private TMP_Text promptText;

        [Tooltip("Root CanvasGroup controlling visibility of the entire interaction prompt.")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Animation")]
        [SerializeField] private bool useFade = true;
        [SerializeField] private float fadeSpeed = 10f;

        private bool _isPromptVisible = false;
        private float _targetAlpha = 0f;
        private string _currentText = string.Empty;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            AutoDiscoverReferences();

            // Start completely hidden
            SetVisibleImmediate(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!useFade || canvasGroup == null) return;

            if (Mathf.Abs(canvasGroup.alpha - _targetAlpha) > 0.005f)
            {
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, _targetAlpha, Time.unscaledDeltaTime * fadeSpeed);

                // Once fully faded out, deactivate game objects so nothing renders
                if (canvasGroup.alpha <= 0.01f && !_isPromptVisible)
                {
                    ApplyElementsActive(false);
                }
            }
        }

        private void AutoDiscoverReferences()
        {
            // CanvasGroup on the root Interaction Canvas ensures all children (Background, Text) fade together
            if (canvasGroup == null)
            {
                if (!TryGetComponent(out canvasGroup))
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            if (promptContainer == null)
            {
                var bg = transform.Find("Background");
                if (bg != null) promptContainer = bg.gameObject;
            }

            if (promptText == null)
            {
                promptText = GetComponentInChildren<TMP_Text>(true);
            }

            // If a child Background had an isolated CanvasGroup from an earlier setup, ensure its alpha is reset
            if (promptContainer != null && promptContainer != gameObject)
            {
                if (promptContainer.TryGetComponent<CanvasGroup>(out var childCg))
                {
                    childCg.alpha = 1f;
                }
            }
        }

        /// <summary>
        /// Displays the interaction prompt with the specified message.
        /// </summary>
        public void Show(string message)
        {
            if (promptText != null && _currentText != message)
            {
                _currentText = message;
                promptText.text = message;
            }

            _isPromptVisible = true;
            _targetAlpha = 1f;

            ApplyElementsActive(true);

            if (!useFade && canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
            }
        }

        /// <summary>
        /// Hides the interaction prompt completely (both background and text).
        /// </summary>
        public void Hide()
        {
            _isPromptVisible = false;
            _targetAlpha = 0f;

            if (!useFade)
            {
                SetVisibleImmediate(false);
            }
        }

        private void SetVisibleImmediate(bool visible)
        {
            _isPromptVisible = visible;
            _targetAlpha = visible ? 1f : 0f;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
            }

            ApplyElementsActive(visible);

            if (!visible && promptText != null)
            {
                promptText.text = string.Empty;
                _currentText = string.Empty;
            }
        }

        private void ApplyElementsActive(bool active)
        {
            if (promptContainer != null && promptContainer != gameObject)
            {
                promptContainer.SetActive(active);
            }

            if (promptText != null)
            {
                promptText.gameObject.SetActive(active);
            }
        }
    }
}
