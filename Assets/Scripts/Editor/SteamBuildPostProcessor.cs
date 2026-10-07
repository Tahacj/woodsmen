using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Woodsmen.Editor
{
    /// <summary>
    /// Automatically copies steam_appid.txt to the standalone build directory upon successful build.
    /// Ensures Steamworks.NET initializes correctly in standalone executable builds.
    /// </summary>
    public static class SteamBuildPostProcessor
    {
        private const string SteamAppIdFileName = "steam_appid.txt";
        private const string DefaultAppId = "480";

        [PostProcessBuild(1)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            try
            {
                if (target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64)
                {
                    string buildDirectory = Path.GetDirectoryName(pathToBuiltProject);
                    if (string.IsNullOrEmpty(buildDirectory) || !Directory.Exists(buildDirectory))
                    {
                        Debug.LogError($"[SteamBuildPostProcessor] Build directory does not exist: '{buildDirectory}'");
                        return;
                    }
                    else
                    {
                        // Valid build directory
                    }

                    string destinationPath = Path.Combine(buildDirectory, SteamAppIdFileName);
                    string projectRootSource = Path.Combine(Application.dataPath, "..", SteamAppIdFileName);

                    if (File.Exists(projectRootSource))
                    {
                        File.Copy(projectRootSource, destinationPath, overwrite: true);
                        Debug.Log($"<color=#55FF55>[SteamBuildPostProcessor] Successfully copied '{SteamAppIdFileName}' from project root to: '{destinationPath}'</color>");
                    }
                    else
                    {
                        File.WriteAllText(destinationPath, DefaultAppId);
                        File.WriteAllText(projectRootSource, DefaultAppId);
                        Debug.Log($"<color=#FFFF55>[SteamBuildPostProcessor] Created default '{SteamAppIdFileName}' with AppID {DefaultAppId} at: '{destinationPath}'</color>");
                    }
                }
                else
                {
                    // Non-Windows standalone target: skipping Steam copy
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamBuildPostProcessor] Exception during post-process build: {ex.Message}");
            }
        }
    }
}
