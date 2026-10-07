using System;
using System.Collections;
using System.IO;
using JustAGame.Core.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JustAGame.UI
{
    /// <summary>
    /// Displays a sleek in-game achievement unlock toast notification.
    /// Listens to EOSPlayerStatsTracker.OnAchievementUnlockedEvent.
    /// Strictly adheres to repository DESIGN_PATTERNS.md zero-slop guidelines.
    /// Fully self-contained: auto-generates UI elements if not explicitly assigned in the inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class AchievementNotificationUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject notificationPanel;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI headerTMP;
        [SerializeField] private TextMeshProUGUI titleTMP;
        [SerializeField] private TextMeshProUGUI descriptionTMP;

        [Header("Icon Configuration")]
        [SerializeField] private Sprite unlockedIconSprite;

        [Header("Timings & Positions")]
        [SerializeField] private float displayDuration = 4.0f;
        [SerializeField] private float fadeDuration = 0.4f;
        [SerializeField] private Vector2 hiddenPosition = new Vector2(0f, 100f);
        [SerializeField] private Vector2 visiblePosition = new Vector2(0f, -40f);

        [Header("Audio (Optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip unlockSound;

        private Coroutine _activeAnimationCoroutine;
        private bool _hasAutoConstructed = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            try
            {
                if (FindFirstObjectByType<AchievementNotificationUI>().IsNull())
                {
                    GameObject runner = new GameObject("AchievementNotificationUI", typeof(AchievementNotificationUI));
                    DontDestroyOnLoad(runner);
                }
                else
                {
                    // Existing instance in scene
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AchievementNotificationUI] Exception in AutoInitialize: {ex.Message}");
            }
        }

        private void Awake()
        {
            try
            {
                EnsureUIComponents();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AchievementNotificationUI] Exception in Awake: {ex.Message}");
            }
        }

        private void OnEnable()
        {
            try
            {
                EOSPlayerStatsTracker.OnAchievementUnlockedEvent += HandleAchievementUnlocked;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AchievementNotificationUI] Exception in OnEnable: {ex.Message}");
            }
        }

        private void OnDisable()
        {
            try
            {
                EOSPlayerStatsTracker.OnAchievementUnlockedEvent -= HandleAchievementUnlocked;

                if (_activeAnimationCoroutine.IsNotNull())
                {
                    StopCoroutine(_activeAnimationCoroutine);
                    _activeAnimationCoroutine = null;
                }
                else
                {
                    // No active coroutine
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AchievementNotificationUI] Exception in OnDisable: {ex.Message}");
            }
        }

        /// <summary>
        /// Public entrypoint to show achievement notification.
        /// </summary>
        public void ShowAchievement(string title, string description, Sprite customIcon = null)
        {
            try
            {
                EnsureUIComponents();

                if (notificationPanel.IsNull() || canvasGroup.IsNull() || panelRect.IsNull())
                {
                    Debug.LogWarning("[AchievementNotificationUI] Required UI components missing.");
                    return;
                }
                else
                {
                    if (titleTMP.IsNotNull())
                    {
                        titleTMP.text = title;
                    }
                    else
                    {
                        // Title TMP not assigned
                    }

                    if (descriptionTMP.IsNotNull())
                    {
                        descriptionTMP.text = description;
                    }
                    else
                    {
                        // Description TMP not assigned
                    }

                    if (iconImage.IsNotNull())
                    {
                        Sprite iconToUse = customIcon.IsNotNull() ? customIcon : unlockedIconSprite;
                        if (iconToUse.IsNotNull())
                        {
                            iconImage.sprite = iconToUse;
                            iconImage.gameObject.SetActive(true);
                        }
                        else
                        {
                            iconImage.gameObject.SetActive(false);
                        }
                    }
                    else
                    {
                        // Icon image not assigned
                    }

                    if (audioSource.IsNotNull() && unlockSound.IsNotNull())
                    {
                        audioSource.PlayOneShot(unlockSound);
                    }
                    else
                    {
                        // No audio configured
                    }

#if UNITY_EDITOR
                Debug.Log($"<color=#FFD700>[AchievementNotificationUI] Showing sliding achievement banner: '{title}' - '{description}'</color>");
#endif

                if (_activeAnimationCoroutine.IsNotNull())
                {
                    StopCoroutine(_activeAnimationCoroutine);
                }
                else
                {
                    // Starting new animation
                }

                _activeAnimationCoroutine = StartCoroutine(AnimateNotificationRoutine());
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AchievementNotificationUI] Exception in ShowAchievement: {ex.Message}");
        }
    }

    private void HandleAchievementUnlocked(string achievementId)
    {
        try
        {
            if (achievementId == EOSPlayerStatsTracker.ACHIEVEMENT_ID_100M)
            {
                ShowAchievement("Century Walker", "You walked 100 meters! [EOS Synced]", unlockedIconSprite);
            }
            else
            {
                ShowAchievement(achievementId, "Achievement Unlocked!", unlockedIconSprite);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AchievementNotificationUI] Exception in HandleAchievementUnlocked: {ex.Message}");
        }
    }

    private IEnumerator AnimateNotificationRoutine()
    {
        notificationPanel.SetActive(true);
        notificationPanel.transform.SetAsLastSibling();
        canvasGroup.alpha = 0f;
        panelRect.anchoredPosition = hiddenPosition;

            // Fade In & Slide Down
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                // Smooth step
                float smoothT = t * t * (3f - 2f * t);

                canvasGroup.alpha = smoothT;
                panelRect.anchoredPosition = Vector2.Lerp(hiddenPosition, visiblePosition, smoothT);
                yield return null;
            }

            canvasGroup.alpha = 1f;
            panelRect.anchoredPosition = visiblePosition;

            // Hold on screen
            yield return new WaitForSecondsRealtime(displayDuration);

            // Fade Out & Slide Up
            elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                float smoothT = t * t * (3f - 2f * t);

                canvasGroup.alpha = 1f - smoothT;
                panelRect.anchoredPosition = Vector2.Lerp(visiblePosition, hiddenPosition, smoothT);
                yield return null;
            }

            canvasGroup.alpha = 0f;
            panelRect.anchoredPosition = hiddenPosition;
            notificationPanel.SetActive(false);
            _activeAnimationCoroutine = null;
        }

        private void EnsureUIComponents()
        {
            try
            {
                if (notificationPanel.IsNotNull())
                {
                    return;
                }
                else
                {
                    // Check if a child panel already exists
                    Transform existing = transform.Find("AchievementNotificationPanel");
                    if (existing.IsNotNull())
                    {
                        notificationPanel = existing.gameObject;
                        panelRect = notificationPanel.GetComponentOrNull<RectTransform>();
                        canvasGroup = notificationPanel.GetComponentOrNull<CanvasGroup>();
                        Transform iconTransform = notificationPanel.transform.Find("Icon");
                        iconImage = iconTransform.IsNotNull() ? iconTransform.GetComponentOrNull<Image>() : null;
                        Transform headerTransform = notificationPanel.transform.Find("Header");
                        headerTMP = headerTransform.IsNotNull() ? headerTransform.GetComponentOrNull<TextMeshProUGUI>() : null;
                        Transform titleTransform = notificationPanel.transform.Find("Title");
                        titleTMP = titleTransform.IsNotNull() ? titleTransform.GetComponentOrNull<TextMeshProUGUI>() : null;
                        Transform descTransform = notificationPanel.transform.Find("Description");
                        descriptionTMP = descTransform.IsNotNull() ? descTransform.GetComponentOrNull<TextMeshProUGUI>() : null;
                        return;
                    }
                    else
                    {
                        if (!_hasAutoConstructed)
                        {
                            ConstructDefaultToastUI();
                        }
                        else
                        {
                            // Already attempted
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AchievementNotificationUI] Exception in EnsureUIComponents: {ex.Message}");
            }
        }

        private void Update()
        {
            try
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
#if ENABLE_INPUT_SYSTEM
                if (UnityEngine.InputSystem.Keyboard.current.IsNotNull() && UnityEngine.InputSystem.Keyboard.current.f7Key.wasPressedThisFrame)
                {
                    TestShowAchievement();
                }
                else
                {
                    // No test key pressed
                }
#else
                if (Input.GetKeyDown(KeyCode.F7))
                {
                    TestShowAchievement();
                }
                else
                {
                    // No test key pressed
                }
#endif
#else
                // In production builds, hotkeys disabled
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AchievementNotificationUI] Exception in Update: {ex.Message}");
            }
        }

        [ContextMenu("Test Slide-In Animation")]
        public void TestShowAchievement()
        {
            ShowAchievement("Century Walker", "You walked 100 meters! [EOS Synced]", unlockedIconSprite);
        }

        private void ConstructDefaultToastUI()
        {
            try
            {
                _hasAutoConstructed = true;

                // Create dedicated top-level overlay canvas so we are never hidden by inactive scene panels
                GameObject canvasObj = new GameObject("AchievementOverlayCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                DontDestroyOnLoad(canvasObj);

                Canvas canvas = canvasObj.GetComponentOrNull<Canvas>();
                if (canvas.IsNotNull())
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 999; // Top of all game UI
                }
                else
                {
                    // Canvas missing
                }

                CanvasScaler scaler = canvasObj.GetComponentOrNull<CanvasScaler>();
                if (scaler.IsNotNull())
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.matchWidthOrHeight = 0.5f;
                }
                else
                {
                    // Scaler missing
                }

                // Create Panel
                notificationPanel = new GameObject("AchievementNotificationPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
                notificationPanel.transform.SetParent(canvasObj.transform, false);

                panelRect = notificationPanel.GetComponentOrNull<RectTransform>();
                if (panelRect.IsNotNull())
                {
                    panelRect.anchorMin = new Vector2(0.5f, 1f);
                    panelRect.anchorMax = new Vector2(0.5f, 1f);
                    panelRect.pivot = new Vector2(0.5f, 1f);
                    panelRect.sizeDelta = new Vector2(420f, 80f);
                    panelRect.anchoredPosition = hiddenPosition;
                }
                else
                {
                    // Panel rect missing
                }

                Image panelBg = notificationPanel.GetComponentOrNull<Image>();
                if (panelBg.IsNotNull())
                {
                    panelBg.color = new Color(0.06f, 0.08f, 0.12f, 0.95f);
                }
                else
                {
                    // Panel background missing
                }

                // Add gold border accent line at top
                GameObject accentObj = new GameObject("GoldAccent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                accentObj.transform.SetParent(notificationPanel.transform, false);
                RectTransform accentRect = accentObj.GetComponentOrNull<RectTransform>();
                if (accentRect.IsNotNull())
                {
                    accentRect.anchorMin = new Vector2(0f, 1f);
                    accentRect.anchorMax = new Vector2(1f, 1f);
                    accentRect.pivot = new Vector2(0.5f, 1f);
                    accentRect.sizeDelta = new Vector2(0f, 3f);
                    accentRect.anchoredPosition = Vector2.zero;
                }
                else
                {
                    // Accent rect missing
                }

                Image accentImg = accentObj.GetComponentOrNull<Image>();
                if (accentImg.IsNotNull())
                {
                    accentImg.color = new Color(1f, 0.84f, 0.0f, 1f);
                }
                else
                {
                    // Accent image missing
                }

                canvasGroup = notificationPanel.GetComponentOrNull<CanvasGroup>();
                if (canvasGroup.IsNotNull())
                {
                    canvasGroup.alpha = 0f;
                    canvasGroup.blocksRaycasts = false;
                }
                else
                {
                    // Canvas group missing
                }

                // Try to load icon from Assets/AchievementIcons/walk_100m_unlocked.png
                if (unlockedIconSprite.IsNull())
                {
                    unlockedIconSprite = TryLoadIconFromDisk("Assets/AchievementIcons/walk_100m_unlocked.png");
                }
                else
                {
                    // Sprite already provided
                }

                // Create Icon Image
                GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconObj.transform.SetParent(notificationPanel.transform, false);
                RectTransform iconRect = iconObj.GetComponentOrNull<RectTransform>();
                if (iconRect.IsNotNull())
                {
                    iconRect.anchorMin = new Vector2(0f, 0.5f);
                    iconRect.anchorMax = new Vector2(0f, 0.5f);
                    iconRect.pivot = new Vector2(0f, 0.5f);
                    iconRect.sizeDelta = new Vector2(56f, 56f);
                    iconRect.anchoredPosition = new Vector2(12f, -1f);
                }
                else
                {
                    // Icon rect missing
                }

                iconImage = iconObj.GetComponentOrNull<Image>();
                if (iconImage.IsNotNull())
                {
                    if (unlockedIconSprite.IsNotNull())
                    {
                        iconImage.sprite = unlockedIconSprite;
                    }
                    else
                    {
                        // Fallback placeholder color
                        iconImage.color = new Color(1f, 0.85f, 0.2f, 1f);
                    }
                }
                else
                {
                    // Icon image missing
                }

                // Create Header TMP
                GameObject headerObj = new GameObject("Header", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                headerObj.transform.SetParent(notificationPanel.transform, false);
                RectTransform headerRect = headerObj.GetComponentOrNull<RectTransform>();
                if (headerRect.IsNotNull())
                {
                    headerRect.anchorMin = new Vector2(0f, 1f);
                    headerRect.anchorMax = new Vector2(1f, 1f);
                    headerRect.pivot = new Vector2(0f, 1f);
                    headerRect.anchoredPosition = new Vector2(76f, -10f);
                    headerRect.sizeDelta = new Vector2(-86f, 18f);
                }
                else
                {
                    // Header rect missing
                }

                headerTMP = headerObj.GetComponentOrNull<TextMeshProUGUI>();
                if (headerTMP.IsNotNull())
                {
                    headerTMP.text = "ACHIEVEMENT UNLOCKED";
                    headerTMP.fontSize = 11f;
                    headerTMP.fontStyle = FontStyles.Bold;
                    headerTMP.color = new Color(1f, 0.84f, 0.0f, 1f);
                }
                else
                {
                    // Header TMP missing
                }

                // Create Title TMP
                GameObject titleObj = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                titleObj.transform.SetParent(notificationPanel.transform, false);
                RectTransform titleRect = titleObj.GetComponentOrNull<RectTransform>();
                if (titleRect.IsNotNull())
                {
                    titleRect.anchorMin = new Vector2(0f, 1f);
                    titleRect.anchorMax = new Vector2(1f, 1f);
                    titleRect.pivot = new Vector2(0f, 1f);
                    titleRect.anchoredPosition = new Vector2(76f, -28f);
                    titleRect.sizeDelta = new Vector2(-86f, 22f);
                }
                else
                {
                    // Title rect missing
                }

                titleTMP = titleObj.GetComponentOrNull<TextMeshProUGUI>();
                if (titleTMP.IsNotNull())
                {
                    titleTMP.text = "Century Walker";
                    titleTMP.fontSize = 15f;
                    titleTMP.fontStyle = FontStyles.Bold;
                    titleTMP.color = Color.white;
                }
                else
                {
                    // Title TMP missing
                }

                // Create Description TMP
                GameObject descObj = new GameObject("Description", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                descObj.transform.SetParent(notificationPanel.transform, false);
                RectTransform descRect = descObj.GetComponentOrNull<RectTransform>();
                if (descRect.IsNotNull())
                {
                    descRect.anchorMin = new Vector2(0f, 1f);
                    descRect.anchorMax = new Vector2(1f, 1f);
                    descRect.pivot = new Vector2(0f, 1f);
                    descRect.anchoredPosition = new Vector2(76f, -48f);
                    descRect.sizeDelta = new Vector2(-86f, 18f);
                }
                else
                {
                    // Desc rect missing
                }

                descriptionTMP = descObj.GetComponentOrNull<TextMeshProUGUI>();
                if (descriptionTMP.IsNotNull())
                {
                    descriptionTMP.text = "You walked 100 meters!";
                    descriptionTMP.fontSize = 11f;
                    descriptionTMP.color = new Color(0.85f, 0.85f, 0.85f, 1f);
                }
                else
                {
                    // Description TMP missing
                }

                    notificationPanel.SetActive(false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AchievementNotificationUI] Exception in ConstructDefaultToastUI: {ex.Message}");
            }
        }

        private Sprite TryLoadIconFromDisk(string relativePath)
        {
            try
            {
                string path1 = Path.Combine(Application.dataPath, "..", relativePath);
                string path2 = Path.Combine(Application.dataPath, relativePath.Replace("Assets/", ""));
                string fullPath = File.Exists(path1) ? path1 : (File.Exists(path2) ? path2 : null);

                if (!string.IsNullOrEmpty(fullPath))
                {
                    byte[] data = File.ReadAllBytes(fullPath);
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (texture.LoadImage(data))
                    {
                        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AchievementNotificationUI] Could not load icon from disk: {ex.Message}");
                return null;
            }
        }
    }
}
