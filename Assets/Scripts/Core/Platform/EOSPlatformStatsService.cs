using System;
using JustAGame.Core.Network;
using JustAGame.Pooling;
using UnityEngine;

namespace JustAGame.Core.Platform
{
    /// <summary>
    /// Production implementation of IPlatformStatsService.
    /// Bridges player metrics, distance calculation, and cloud achievement synchronization with EOS.
    /// </summary>
    public sealed class EOSPlatformStatsService : IPlatformStatsService
    {
        public float TotalDistanceWalked
        {
            get
            {
                if (EOSPlayerStatsTracker.LocalInstance.IsNotNull())
                {
                    return EOSPlayerStatsTracker.LocalInstance.TotalDistanceWalked;
                }
                else
                {
                    return 0f;
                }
            }
        }

        public bool IsCenturyWalkerUnlocked
        {
            get
            {
                if (EOSPlayerStatsTracker.LocalInstance.IsNotNull())
                {
                    return EOSPlayerStatsTracker.LocalInstance.IsAchievementUnlocked;
                }
                else
                {
                    return false;
                }
            }
        }

        public bool IsBackendSynced
        {
            get
            {
                if (EOSPlayerStatsTracker.LocalInstance.IsNotNull())
                {
                    return EOSPlayerStatsTracker.LocalInstance.IsAchievementBackendSynced;
                }
                else
                {
                    return false;
                }
            }
        }

        public event Action<float, float> OnDistanceUpdated;
        public event Action<string> OnAchievementUnlocked;
        public event Action<bool> OnSyncStatusChanged;

        public EOSPlatformStatsService()
        {
            try
            {
                EOSPlayerStatsTracker.OnDistanceUpdated += HandleDistanceUpdated;
                EOSPlayerStatsTracker.OnAchievementUnlockedEvent += HandleAchievementUnlocked;
                EOSPlayerStatsTracker.OnAchievementSyncStatusChanged += HandleSyncStatusChanged;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformStatsService] Exception in constructor: {ex.Message}");
            }
        }

        public void IngestDistanceMetric(int meters)
        {
            try
            {
                if (EOSPlayerStatsTracker.LocalInstance.IsNotNull())
                {
                    EOSPlayerStatsTracker.LocalInstance.IngestDistanceMetric(meters);
                }
                else
                {
                    // Tracker not spawned yet
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformStatsService] Exception in IngestDistanceMetric: {ex.Message}");
            }
        }

        public void TriggerAchievementUnlock(string achievementId)
        {
            try
            {
                if (EOSPlayerStatsTracker.LocalInstance.IsNotNull())
                {
                    EOSPlayerStatsTracker.LocalInstance.TriggerAchievementUnlock();
                }
                else
                {
                    // Tracker not spawned yet
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformStatsService] Exception in TriggerAchievementUnlock: {ex.Message}");
            }
        }

        public void QueryStatsStatus()
        {
            try
            {
                if (EOSPlayerStatsTracker.LocalInstance.IsNotNull())
                {
                    EOSPlayerStatsTracker.LocalInstance.QueryStatsStatus();
                }
                else
                {
                    // Tracker not spawned yet
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformStatsService] Exception in QueryStatsStatus: {ex.Message}");
            }
        }

        public void QueryAchievementsStatus()
        {
            try
            {
                if (EOSPlayerStatsTracker.LocalInstance.IsNotNull())
                {
                    EOSPlayerStatsTracker.LocalInstance.QueryAchievementsStatus();
                }
                else
                {
                    // Tracker not spawned yet
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformStatsService] Exception in QueryAchievementsStatus: {ex.Message}");
            }
        }

        public void ResetLocalProgress()
        {
            try
            {
                if (EOSPlayerStatsTracker.LocalInstance.IsNotNull())
                {
                    EOSPlayerStatsTracker.LocalInstance.ResetLocalProgress();
                }
                else
                {
                    // Tracker not spawned yet
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformStatsService] Exception in ResetLocalProgress: {ex.Message}");
            }
        }

        private void HandleDistanceUpdated(float walked, float target)
        {
            try
            {
                OnDistanceUpdated?.Invoke(walked, target);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformStatsService] Exception in HandleDistanceUpdated: {ex.Message}");
            }
        }

        private void HandleAchievementUnlocked(string id)
        {
            try
            {
                OnAchievementUnlocked?.Invoke(id);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformStatsService] Exception in HandleAchievementUnlocked: {ex.Message}");
            }
        }

        private void HandleSyncStatusChanged(bool synced)
        {
            try
            {
                OnSyncStatusChanged?.Invoke(synced);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformStatsService] Exception in HandleSyncStatusChanged: {ex.Message}");
            }
        }
    }
}
