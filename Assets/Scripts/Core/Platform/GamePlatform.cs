using System;
using UnityEngine;

namespace JustAGame.Core.Platform
{
    /// <summary>
    /// Unified static facade providing clean, modular access to the JustAGame platform services.
    /// Decouples game logic from underlying EOS SDK, Steamworks, and Mirror transport implementations.
    /// Standardized entry point for external interfaces, launchers, and future multiplayer game titles.
    /// </summary>
    public static class GamePlatform
    {
        public const string PlatformVersion = "1.0.0-robust";

        private static IPlatformAuthService _auth;
        private static IPlatformNetworkService _network;
        private static IPlatformStatsService _stats;
        private static bool _initialized = false;

        public static bool IsInitialized => _initialized;

        public static IPlatformAuthService Auth
        {
            get
            {
                EnsureInitialized();
                return _auth;
            }
        }

        public static IPlatformNetworkService Network
        {
            get
            {
                EnsureInitialized();
                return _network;
            }
        }

        /// <summary>
        /// External-facing network manager contract for UI launchers and future platform integrations.
        /// Provides StartLocal, ConnectLocal, StartRemote, JoinRemote, and GetCode operations.
        /// Returns null if the underlying network service does not implement INetworkManager.
        /// </summary>
        public static INetworkManager NetworkManager
        {
            get
            {
                EnsureInitialized();
                return _network as INetworkManager;
            }
        }

        public static IPlatformStatsService Stats
        {
            get
            {
                EnsureInitialized();
                return _stats;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            try
            {
                if (_initialized)
                {
                    return;
                }
                else
                {
                    _auth = new EOSPlatformAuthService();
                    _network = new EOSPlatformNetworkService();
                    _stats = new EOSPlatformStatsService();
                    _initialized = true;

#if UNITY_EDITOR
                    Debug.Log($"<color=#55FF55>[GamePlatform] JustAGame Platform Services v{PlatformVersion} initialized successfully.</color>");
#endif
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GamePlatform] Exception in Initialize: {ex.Message}");
            }
        }

        private static void EnsureInitialized()
        {
            try
            {
                if (!_initialized)
                {
                    Initialize();
                }
                else
                {
                    // Already initialized
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GamePlatform] Exception in EnsureInitialized: {ex.Message}");
            }
        }
    }
}
