using System;
using Epic.OnlineServices;
using EpicTransport;
using JustAGame.Core.Network;
using UnityEditor;
using UnityEngine;

namespace JustAGame.Editor
{
    /// <summary>
    /// Editor utilities for resetting local walking distance cache, deleting Guest Device ID,
    /// and managing EOS testing profiles. Adheres to repository DESIGN_PATTERNS.md.
    /// </summary>
    public static class EOSDebugTools
    {
        [MenuItem("EOS Tools/Reset Local Walking Distance Cache")]
        public static void ResetLocalDistanceCache()
        {
            try
            {
                if (EOSPlayerStatsTracker.LocalInstance.IsNotNull())
                {
                    EOSPlayerStatsTracker.LocalInstance.ResetLocalProgress();
                }
                else
                {
                    PlayerPrefs.DeleteKey("EOS_LocalDistance");
                    PlayerPrefs.DeleteKey("EOS_LocalAchUnlocked");
                    PlayerPrefs.DeleteKey("EOS_LocalAchSynced");

                    string currentPuid = EOSSDKComponent.LocalUserProductIdString;
                    if (!string.IsNullOrEmpty(currentPuid))
                    {
                        PlayerPrefs.DeleteKey($"EOS_Distance_{currentPuid}");
                        PlayerPrefs.DeleteKey($"EOS_AchUnlocked_{currentPuid}");
                        PlayerPrefs.DeleteKey($"EOS_AchSynced_{currentPuid}");
                    }
                    else
                    {
                        // No active session
                    }

                    PlayerPrefs.Save();
                    Debug.Log("[EOS Tools] Local distance PlayerPrefs keys deleted successfully!");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOS Tools] Exception in ResetLocalDistanceCache: {ex.Message}");
            }
        }

        [MenuItem("EOS Tools/Reset Guest Device ID (Create Fresh 0m Account)")]
        public static void ResetGuestDeviceId()
        {
            try
            {
                ResetLocalDistanceCache();

                if (EOSSDKComponent.Initialized)
                {
                    EOSSDKComponent.DeleteGuestDeviceId((Result result) =>
                    {
                        if (result == Result.Success)
                        {
                            Debug.Log("<color=#55FF55>[EOS Tools] Guest Device ID removed from Windows keychain! Next Guest Login will create a brand new ProductUserId (0m walked).</color>");
                        }
                        else
                        {
                            Debug.LogWarning($"[EOS Tools] DeleteDeviceId returned: {result}");
                        }
                    });
                }
                else
                {
                    Debug.Log("[EOS Tools] Local cache cleared. Enter Play mode and click Guest Login to test.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOS Tools] Exception in ResetGuestDeviceId: {ex.Message}");
            }
        }

        [MenuItem("EOS Tools/Print Current Player ProductUserId (PUID)")]
        public static void PrintCurrentPuid()
        {
            try
            {
                string puid = EOSSDKComponent.LocalUserProductIdString;
                if (!string.IsNullOrEmpty(puid))
                {
                    Debug.Log($"<color=#55FF55>[EOS Tools] Current ProductUserId: {puid}</color>\nTo wipe cloud stats/achievements for this user on Epic's servers:\n1. Open Epic Developer Portal > Epic Online Services > Player Search\n2. Search PUID '{puid}'\n3. Click 'Delete Player Data'");
                }
                else
                {
                    Debug.LogWarning("[EOS Tools] No active EOS user logged in. Enter Play mode and log in first.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOS Tools] Exception in PrintCurrentPuid: {ex.Message}");
            }
        }

        [MenuItem("EOS Tools/Test Sliding Achievement Popup (F7)")]
        public static void TestSlidingAchievement()
        {
            try
            {
                var ui = UnityEngine.Object.FindFirstObjectByType<JustAGame.UI.AchievementNotificationUI>();
                if (ui.IsNotNull())
                {
                    ui.TestShowAchievement();
                }
                else
                {
                    Debug.LogWarning("[EOS Tools] Enter Play mode first to preview the sliding achievement popup.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOS Tools] Exception in TestSlidingAchievement: {ex.Message}");
            }
        }
    }
}
