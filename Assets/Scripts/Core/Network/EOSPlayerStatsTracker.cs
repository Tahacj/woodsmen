using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Achievements;
using Epic.OnlineServices.Stats;
using EpicTransport;
using JustAGame;
using UnityEngine;

namespace JustAGame.Core.Network
{
    /// <summary>
    /// Tracks client authoritative walking distance, ingests distance metrics into EOS Stats Interface,
    /// and manages the 100m walking achievement through the EOS Achievements Interface.
    /// Follows strict repository DESIGN_PATTERNS.md zero-slop guidelines.
    /// Pure MonoBehaviour attached to the local player to guarantee zero Mirror netIdentity NREs.
    /// </summary>
    [DisallowMultipleComponent]
    public class EOSPlayerStatsTracker : MonoBehaviour
    {
        public const string STAT_NAME_DISTANCE = "DISTANCE_WALKED";
        public const string ACHIEVEMENT_ID_100M = "WALK_100M";
        public const float ACHIEVEMENT_TARGET_DISTANCE = 100.0f;
        public const float METRIC_INGEST_INTERVAL_METERS = 5.0f;

        [Header("Distance Tracking")]
        [SerializeField] private float totalDistanceWalked = 0f;
        [SerializeField] private bool achievementUnlocked = false;
        [SerializeField] private bool isAchievementBackendSynced = false;

        public float TotalDistanceWalked => totalDistanceWalked;
        public bool IsAchievementUnlocked => achievementUnlocked;
        public bool IsAchievementBackendSynced => isAchievementBackendSynced;

        public static EOSPlayerStatsTracker LocalInstance { get; private set; }

        public static event Action<float, float> OnDistanceUpdated;
        public static event Action<string> OnAchievementUnlockedEvent;
        public static event Action<bool> OnAchievementSyncStatusChanged;

        private float _unreportedDistance = 0f;
        private ulong _notifyUnlockId = 0;
        private string _lastSyncedPuid = string.Empty;

        public void InitializeLocal()
        {
            try
            {
                LocalInstance = this;

                // Instantly restore cached distance from previous sessions on this machine
                RestoreCachedDistance();

                // Notify UI listeners with current distance
                OnDistanceUpdated?.Invoke(totalDistanceWalked, ACHIEVEMENT_TARGET_DISTANCE);

                // Fetch authoritative stats and achievements from EOS Cloud if already initialized
                if (EOSSDKComponent.Initialized && EOSSDKComponent.LocalUserProductId.IsNotNull())
                {
                    _lastSyncedPuid = EOSSDKComponent.LocalUserProductIdString;
                    QueryStatsStatus();
                    QueryAchievementsStatus();
                    SubscribeToUnlockNotifications();
                }
                else
                {
                    // EOS login deferred (waiting for EOSLoginUI or background auth)
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in InitializeLocal: {ex.Message}");
            }
        }

        private void Awake()
        {
            try
            {
                if (LocalInstance.IsNull())
                {
                    LocalInstance = this;
                }
                else
                {
                    // Existing instance registered
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in Awake: {ex.Message}");
            }
        }

        private void Start()
        {
            try
            {
                // Secondary check if EOS initialized after local player spawned
                if (EOSSDKComponent.Initialized && EOSSDKComponent.LocalUserProductId.IsNotNull())
                {
                    _lastSyncedPuid = EOSSDKComponent.LocalUserProductIdString;
                    RestoreCachedDistance();
                    QueryStatsStatus();
                    QueryAchievementsStatus();
                    SubscribeToUnlockNotifications();
                }
                else
                {
                    // Notification already active or EOS not ready
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in Start: {ex.Message}");
            }
        }

        private void Update()
        {
            try
            {
                // Monitor EOSSDKComponent for deferred login initialization (e.g. from EOSLoginUI)
                if (EOSSDKComponent.Initialized && EOSSDKComponent.LocalUserProductId.IsNotNull())
                {
                    string currentPuid = EOSSDKComponent.LocalUserProductIdString;
                    if (currentPuid != _lastSyncedPuid && !string.IsNullOrEmpty(currentPuid))
                    {
                        _lastSyncedPuid = currentPuid;
#if UNITY_EDITOR
                        Debug.Log($"[EOSPlayerStatsTracker] Active EOS session detected for user: {currentPuid}. Synchronizing cloud stats and achievements...");
#endif

                        RestoreCachedDistance();
                        QueryStatsStatus();
                        QueryAchievementsStatus();
                        SubscribeToUnlockNotifications();

                        if (_unreportedDistance > 0f)
                        {
                            IngestDistanceMetric(Mathf.CeilToInt(_unreportedDistance));
                            _unreportedDistance = 0f;
                        }
                        else
                        {
                            // No pending unreported distance
                        }
                    }
                    else
                    {
                        // PUID session unchanged
                    }
                }
                else
                {
                    // EOS not yet logged in / initialized
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
#if ENABLE_INPUT_SYSTEM
                if (UnityEngine.InputSystem.Keyboard.current.IsNotNull() && UnityEngine.InputSystem.Keyboard.current.f9Key.wasPressedThisFrame)
                {
                    ResetLocalProgress();
                }
                else
                {
                    // No reset key pressed
                }
#else
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    ResetLocalProgress();
                }
                else
                {
                    // No reset key pressed
                }
#endif
#else
                // Hotkeys disabled in production builds
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in Update: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the local walking distance and achievement status back to 0m (RED).
        /// Can be triggered via Inspector context menu during play mode or via F9 key.
        /// </summary>
        [ContextMenu("Reset Local Distance Progress (0m)")]
        public void ResetLocalProgress()
        {
            try
            {
                totalDistanceWalked = 0f;
                achievementUnlocked = false;
                isAchievementBackendSynced = false;
                _unreportedDistance = 0f;

                string puid = EOSSDKComponent.LocalUserProductIdString;
                string key = string.IsNullOrEmpty(puid) ? "EOS_LocalDistance" : $"EOS_Distance_{puid}";
                string unlockKey = string.IsNullOrEmpty(puid) ? "EOS_LocalAchUnlocked" : $"EOS_AchUnlocked_{puid}";
                string syncKey = string.IsNullOrEmpty(puid) ? "EOS_LocalAchSynced" : $"EOS_AchSynced_{puid}";

                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.DeleteKey(unlockKey);
                PlayerPrefs.DeleteKey(syncKey);
                PlayerPrefs.DeleteKey("EOS_LocalDistance");
                PlayerPrefs.DeleteKey("EOS_LocalAchUnlocked");
                PlayerPrefs.DeleteKey("EOS_LocalAchSynced");
                PlayerPrefs.Save();

                OnDistanceUpdated?.Invoke(0f, ACHIEVEMENT_TARGET_DISTANCE);
                OnAchievementSyncStatusChanged?.Invoke(false);

#if UNITY_EDITOR
                Debug.Log("[EOSPlayerStatsTracker] Local distance and achievement progress successfully reset to 0.0m (RED).");
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in ResetLocalProgress: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            try
            {
                UnsubscribeFromUnlockNotifications();

                if (LocalInstance == this)
                {
                    LocalInstance = null;
                }
                else
                {
                    // Secondary instance
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in OnDestroy: {ex.Message}");
            }
        }

        /// <summary>
        /// Accumulates walked distance in meters. Called by ClientAuthoritativeMovement on movement.
        /// </summary>
        public void AddWalkedDistance(float distanceInMeters)
        {
            try
            {
                if (distanceInMeters <= 0.0001f || distanceInMeters > 50.0f)
                {
                    // Discard negligible or extreme snap/teleport distances
                    return;
                }
                else
                {
                    totalDistanceWalked += distanceInMeters;
                    _unreportedDistance += distanceInMeters;

                    // Notify UI listeners
                    OnDistanceUpdated?.Invoke(totalDistanceWalked, ACHIEVEMENT_TARGET_DISTANCE);

                    // Milestone check for 100m achievement
                    if (totalDistanceWalked >= ACHIEVEMENT_TARGET_DISTANCE && !achievementUnlocked)
                    {
                        achievementUnlocked = true;
                        TriggerAchievementUnlock();
                    }
                    else
                    {
                        // Milestone not reached or already unlocked
                    }

                    // Ingest stat in batches to prevent network spam
                    if (_unreportedDistance >= METRIC_INGEST_INTERVAL_METERS)
                    {
                        IngestDistanceMetric((int)_unreportedDistance);
                        _unreportedDistance = 0f;
                    }
                    else
                    {
                        // Accumulating distance for next batch
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in AddWalkedDistance: {ex.Message}");
            }
        }

        private void SetBackendSynced(bool synced)
        {
            try
            {
                if (isAchievementBackendSynced != synced)
                {
                    isAchievementBackendSynced = synced;
                    OnAchievementSyncStatusChanged?.Invoke(synced);
                    SaveCachedDistance();
                }
                else
                {
                    // Sync status unchanged
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in SetBackendSynced: {ex.Message}");
            }
        }

        public void TriggerAchievementUnlock()
        {
            try
            {
#if UNITY_EDITOR
                Debug.Log("[EOSPlayerStatsTracker] [ACHIEVEMENT UNLOCKED] Century Walker (100m Walked)!");
#endif
                OnAchievementUnlockedEvent?.Invoke(ACHIEVEMENT_ID_100M);

                // Flush any remaining unreported distance so backend stat evaluation triggers immediately
                if (_unreportedDistance > 0.0f)
                {
                    IngestDistanceMetric(Mathf.CeilToInt(_unreportedDistance));
                    _unreportedDistance = 0f;
                }
                else
                {
                    // Stat is up to date
                }

                if (!EOSSDKComponent.Initialized)
                {
                    Debug.LogWarning("[EOSPlayerStatsTracker] EOS SDK not initialized. Local achievement registered.");
                    return;
                }
                else
                {
                    ProductUserId localPuid = EOSSDKComponent.LocalUserProductId;
                    if (localPuid.IsNull())
                    {
                        Debug.LogWarning("[EOSPlayerStatsTracker] Local ProductUserId is null. Local achievement registered.");
                        return;
                    }
                    else
                    {
                        var achievementsInterface = EOSSDKComponent.GetAchievementsInterface();
                        if (achievementsInterface.IsNull())
                        {
                            Debug.LogWarning("[EOSPlayerStatsTracker] AchievementsInterface is null.");
                            return;
                        }
                        else
                        {
                            var unlockOptions = new UnlockAchievementsOptions
                            {
                                UserId = localPuid,
                                AchievementIds = new string[] { ACHIEVEMENT_ID_100M }
                            };

                            achievementsInterface.UnlockAchievements(unlockOptions, null, (OnUnlockAchievementsCompleteCallbackInfo callbackInfo) =>
                            {
                                try
                                {
                                    if (callbackInfo.ResultCode == Result.Success)
                                    {
#if UNITY_EDITOR
                                        Debug.Log($"[EOSPlayerStatsTracker] Successfully pushed unlock for '{ACHIEVEMENT_ID_100M}' to EOS Backend!");
#endif
                                        SetBackendSynced(true);
                                    }
                                    else if (callbackInfo.ResultCode == Result.NoChange || callbackInfo.ResultCode == Result.DuplicateNotAllowed)
                                    {
                                        SetBackendSynced(true);
                                    }
                                    else if (callbackInfo.ResultCode == Result.NotConfigured)
                                    {
                                        QueryAchievementsStatus();
                                    }
                                    else
                                    {
                                        // Other backend response
                                    }
                                }
                                catch (Exception cbEx)
                                {
                                    Debug.LogError($"[EOSPlayerStatsTracker] Exception in UnlockAchievements callback: {cbEx.Message}");
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in TriggerAchievementUnlock: {ex.Message}");
            }
        }

        public void IngestDistanceMetric(int metersWalked)
        {
            try
            {
                if (metersWalked <= 0)
                {
                    return;
                }
                else
                {
#if UNITY_EDITOR
                    Debug.Log($"[EOSPlayerStatsTracker] Ingesting distance metric: +{metersWalked}m (Total: {totalDistanceWalked:F1}m)");
#endif
                    // Always ensure local cache is updated immediately to guarantee zero data loss
                    SaveCachedDistance();

                    if (!EOSSDKComponent.Initialized)
                    {
                        return;
                    }
                    else
                    {
                        ProductUserId localPuid = EOSSDKComponent.LocalUserProductId;
                        if (localPuid.IsNull())
                        {
                            return;
                        }
                        else
                        {
                            var statsInterface = EOSSDKComponent.GetStatsInterface();
                            if (statsInterface.IsNull())
                            {
                                return;
                            }
                            else
                            {
                                var ingestOptions = new IngestStatOptions
                                {
                                    LocalUserId = localPuid,
                                    TargetUserId = localPuid,
                                    Stats = new IngestData[]
                                    {
                                        new IngestData
                                        {
                                            StatName = STAT_NAME_DISTANCE,
                                            IngestAmount = metersWalked
                                        }
                                    }
                                };

                                statsInterface.IngestStat(ingestOptions, null, (IngestStatCompleteCallbackInfo callbackInfo) =>
                                {
                                    try
                                    {
                                        if (callbackInfo.ResultCode == Result.Success)
                                        {
                                            // If distance milestone reached, verify backend stat rule evaluation
                                            if (totalDistanceWalked >= ACHIEVEMENT_TARGET_DISTANCE && !isAchievementBackendSynced)
                                            {
                                                QueryAchievementsStatus();
                                            }
                                            else
                                            {
                                                // Achievement already synced or milestone not yet reached
                                            }
                                        }
                                        else
                                        {
                                            // Backend stat ingest failed
                                        }
                                    }
                                    catch (Exception cbEx)
                                    {
                                        Debug.LogError($"[EOSPlayerStatsTracker] Exception in IngestStat callback: {cbEx.Message}");
                                    }
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in IngestDistanceMetric: {ex.Message}");
            }
        }

        private void SubscribeToUnlockNotifications()
        {
            try
            {
                if (!EOSSDKComponent.Initialized || _notifyUnlockId != 0)
                {
                    return;
                }
                else
                {
                    var achievementsInterface = EOSSDKComponent.GetAchievementsInterface();
                    if (achievementsInterface.IsNull())
                    {
                        return;
                    }
                    else
                    {
                        var options = new AddNotifyAchievementsUnlockedV2Options();
                        _notifyUnlockId = achievementsInterface.AddNotifyAchievementsUnlockedV2(options, null, (OnAchievementsUnlockedCallbackV2Info callbackInfo) =>
                        {
                            try
                            {
                                if (callbackInfo.AchievementId == ACHIEVEMENT_ID_100M)
                                {
                                    achievementUnlocked = true;
                                    SetBackendSynced(true);
                                    OnAchievementUnlockedEvent?.Invoke(callbackInfo.AchievementId);
#if UNITY_EDITOR
                                    Debug.Log($"[EOSPlayerStatsTracker] EOS Backend verified achievement unlock: {callbackInfo.AchievementId}!");
#endif
                                }
                                else
                                {
                                    OnAchievementUnlockedEvent?.Invoke(callbackInfo.AchievementId);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[EOSPlayerStatsTracker] Exception in OnAchievementsUnlockedCallbackV2: {ex.Message}");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in SubscribeToUnlockNotifications: {ex.Message}");
            }
        }

        private void UnsubscribeFromUnlockNotifications()
        {
            try
            {
                if (_notifyUnlockId != 0 && EOSSDKComponent.Initialized)
                {
                    var achievementsInterface = EOSSDKComponent.GetAchievementsInterface();
                    if (achievementsInterface.IsNotNull())
                    {
                        achievementsInterface.RemoveNotifyAchievementsUnlocked(_notifyUnlockId);
                    }
                    else
                    {
                        // AchievementsInterface null
                    }
                    _notifyUnlockId = 0;
                }
                else
                {
                    // Not subscribed or EOS uninitialized
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in UnsubscribeFromUnlockNotifications: {ex.Message}");
            }
        }

        public void QueryAchievementsStatus()
        {
            try
            {
                if (!EOSSDKComponent.Initialized)
                {
                    return;
                }
                else
                {
                    ProductUserId localPuid = EOSSDKComponent.LocalUserProductId;
                    if (localPuid.IsNull())
                    {
                        return;
                    }
                    else
                    {
                        var achievementsInterface = EOSSDKComponent.GetAchievementsInterface();
                        if (achievementsInterface.IsNull())
                        {
                            return;
                        }
                        else
                        {
                            var options = new QueryPlayerAchievementsOptions
                            {
                                LocalUserId = localPuid,
                                TargetUserId = localPuid
                            };

                            achievementsInterface.QueryPlayerAchievements(options, null, (OnQueryPlayerAchievementsCompleteCallbackInfo callbackInfo) =>
                            {
                                try
                                {
                                    if (callbackInfo.ResultCode == Result.Success)
                                    {
                                        var copyOptions = new CopyPlayerAchievementByAchievementIdOptions
                                        {
                                            LocalUserId = localPuid,
                                            TargetUserId = localPuid,
                                            AchievementId = ACHIEVEMENT_ID_100M
                                        };

                                        Result copyResult = achievementsInterface.CopyPlayerAchievementByAchievementId(copyOptions, out PlayerAchievement playerAch);
                                        if (copyResult == Result.Success && playerAch.IsNotNull())
                                        {
                                            if (playerAch.UnlockTime.HasValue || playerAch.Progress >= 100.0)
                                            {
                                                achievementUnlocked = true;
                                                totalDistanceWalked = Mathf.Max(totalDistanceWalked, ACHIEVEMENT_TARGET_DISTANCE);
                                                SetBackendSynced(true);
                                                SaveCachedDistance();
                                                OnDistanceUpdated?.Invoke(totalDistanceWalked, ACHIEVEMENT_TARGET_DISTANCE);
#if UNITY_EDITOR
                                                Debug.Log($"[EOSPlayerStatsTracker] Achievement '{ACHIEVEMENT_ID_100M}' is verified UNLOCKED on EOS backend.");
#endif
                                            }
                                            else
                                            {
                                                // Achievement in progress on backend
                                            }
                                        }
                                        else
                                        {
                                            // Achievement not yet unlocked on backend
                                        }
                                    }
                                    else
                                    {
                                        // Query failed
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogError($"[EOSPlayerStatsTracker] Exception in QueryPlayerAchievements callback: {ex.Message}");
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in QueryAchievementsStatus: {ex.Message}");
            }
        }

        public void QueryStatsStatus()
        {
            try
            {
                if (!EOSSDKComponent.Initialized)
                {
                    return;
                }
                else
                {
                    ProductUserId localPuid = EOSSDKComponent.LocalUserProductId;
                    if (localPuid.IsNull())
                    {
                        return;
                    }
                    else
                    {
                        var statsInterface = EOSSDKComponent.GetStatsInterface();
                        if (statsInterface.IsNull())
                        {
                            return;
                        }
                        else
                        {
                            var options = new QueryStatsOptions
                            {
                                LocalUserId = localPuid,
                                TargetUserId = localPuid,
                                StatNames = new string[] { STAT_NAME_DISTANCE }
                            };

                            statsInterface.QueryStats(options, null, (OnQueryStatsCompleteCallbackInfo callbackInfo) =>
                            {
                                try
                                {
                                    if (callbackInfo.ResultCode == Result.Success)
                                    {
                                        var copyOptions = new CopyStatByNameOptions
                                        {
                                            TargetUserId = localPuid,
                                            Name = STAT_NAME_DISTANCE
                                        };

                                        Result copyResult = statsInterface.CopyStatByName(copyOptions, out Stat stat);
                                        if (copyResult == Result.Success && stat.IsNotNull())
                                        {
                                            float cloudDistance = (float)stat.Value;
#if UNITY_EDITOR
                                            Debug.Log($"[EOSPlayerStatsTracker] Fetched cloud distance: {cloudDistance}m (Local: {totalDistanceWalked:F1}m)");
#endif

                                            if (cloudDistance > totalDistanceWalked)
                                            {
                                                totalDistanceWalked = cloudDistance;
                                            }
                                            else
                                            {
                                                // Local distance already at or above cloud snapshot
                                            }

                                            if (totalDistanceWalked >= ACHIEVEMENT_TARGET_DISTANCE)
                                            {
                                                achievementUnlocked = true;
                                                if (!isAchievementBackendSynced)
                                                {
                                                    QueryAchievementsStatus();
                                                }
                                                else
                                                {
                                                    // Already verified synced
                                                }
                                            }
                                            else
                                            {
                                                // Target not yet reached
                                            }

                                            SaveCachedDistance();
                                            OnDistanceUpdated?.Invoke(totalDistanceWalked, ACHIEVEMENT_TARGET_DISTANCE);
                                        }
                                        else
                                        {
                                            // Stat has no recorded value yet
                                        }
                                    }
                                    else
                                    {
                                        // QueryStats failed
                                    }
                                }
                                catch (Exception cbEx)
                                {
                                    Debug.LogError($"[EOSPlayerStatsTracker] Exception in QueryStats callback: {cbEx.Message}");
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in QueryStatsStatus: {ex.Message}");
            }
        }

        private void RestoreCachedDistance()
        {
            try
            {
                string puid = EOSSDKComponent.LocalUserProductIdString;
                string key = string.IsNullOrEmpty(puid) ? "EOS_LocalDistance" : $"EOS_Distance_{puid}";
                string unlockKey = string.IsNullOrEmpty(puid) ? "EOS_LocalAchUnlocked" : $"EOS_AchUnlocked_{puid}";
                string syncKey = string.IsNullOrEmpty(puid) ? "EOS_LocalAchSynced" : $"EOS_AchSynced_{puid}";

                float cached = PlayerPrefs.GetFloat(key, 0f);
                bool cachedUnlocked = PlayerPrefs.GetInt(unlockKey, 0) == 1;
                bool cachedSynced = PlayerPrefs.GetInt(syncKey, 0) == 1;

                if (cached > totalDistanceWalked)
                {
                    totalDistanceWalked = cached;
                }
                else
                {
                    // Existing is higher or equal
                }

                if (cachedUnlocked || totalDistanceWalked >= ACHIEVEMENT_TARGET_DISTANCE)
                {
                    achievementUnlocked = true;
                }
                else
                {
                    // Under target
                }

                if (cachedSynced)
                {
                    SetBackendSynced(true);
                }
                else
                {
                    // Not verified synced yet
                }

                OnDistanceUpdated?.Invoke(totalDistanceWalked, ACHIEVEMENT_TARGET_DISTANCE);
#if UNITY_EDITOR
                Debug.Log($"[EOSPlayerStatsTracker] Restored cached profile: Dist={totalDistanceWalked:F1}m, Unlocked={achievementUnlocked}, Synced={isAchievementBackendSynced} for {key}");
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in RestoreCachedDistance: {ex.Message}");
            }
        }

        private void SaveCachedDistance()
        {
            try
            {
                string puid = EOSSDKComponent.LocalUserProductIdString;
                string key = string.IsNullOrEmpty(puid) ? "EOS_LocalDistance" : $"EOS_Distance_{puid}";
                string unlockKey = string.IsNullOrEmpty(puid) ? "EOS_LocalAchUnlocked" : $"EOS_AchUnlocked_{puid}";
                string syncKey = string.IsNullOrEmpty(puid) ? "EOS_LocalAchSynced" : $"EOS_AchSynced_{puid}";

                PlayerPrefs.SetFloat(key, totalDistanceWalked);
                PlayerPrefs.SetInt(unlockKey, achievementUnlocked ? 1 : 0);
                PlayerPrefs.SetInt(syncKey, isAchievementBackendSynced ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in SaveCachedDistance: {ex.Message}");
            }
        }

        private void OnApplicationQuit()
        {
            try
            {
                if (_unreportedDistance > 0f)
                {
                    IngestDistanceMetric(Mathf.CeilToInt(_unreportedDistance));
                    _unreportedDistance = 0f;
                }
                else
                {
                    // Up to date
                }

                SaveCachedDistance();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlayerStatsTracker] Exception in OnApplicationQuit: {ex.Message}");
            }
        }
    }
}
