using Epic.OnlineServices.Logging;
using System;
using UnityEngine;

namespace EpicTransport {
    public static class Logger {

        public static void EpicDebugLog(LogMessage message) {
#if UNITY_EDITOR
            switch (message.Level) {
                case LogLevel.Error:
                    Debug.LogError($"[EOS SDK] [{message.Category}] {message.Message}");
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning($"[EOS SDK] [{message.Category}] {message.Message}");
                    break;
                case LogLevel.Fatal:
                    Debug.LogException(new Exception($"[EOS SDK] [{message.Category}] {message.Message}"));
                    break;
                case LogLevel.Info:
                    // Only log high-level info in Editor, ignore verbose message spam
                    break;
                default:
                    break;
            }
#else
            if (message.Level == LogLevel.Fatal || message.Level == LogLevel.Error) {
                Debug.LogError($"[EOS SDK] [{message.Category}] {message.Message}");
            }
#endif
        }
    }
}