using System;

namespace JustAGame.Core.Platform
{
    /// <summary>
    /// Service contract for player stats and achievements synchronized with EOS backend.
    /// </summary>
    public interface IPlatformStatsService
    {
        float TotalDistanceWalked { get; }
        bool IsCenturyWalkerUnlocked { get; }
        bool IsBackendSynced { get; }

        event Action<float, float> OnDistanceUpdated;
        event Action<string> OnAchievementUnlocked;
        event Action<bool> OnSyncStatusChanged;

        void IngestDistanceMetric(int meters);
        void TriggerAchievementUnlock(string achievementId);
        void QueryStatsStatus();
        void QueryAchievementsStatus();
        void ResetLocalProgress();
    }
}
