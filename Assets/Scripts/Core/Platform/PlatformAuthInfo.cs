using System;

namespace JustAGame.Core.Platform
{
    public enum PlatformAuthType
    {
        None = 0,
        DeviceId = 1,
        Steam = 2,
        DevAuthTool = 3,
        EpicAccountPortal = 4
    }

    /// <summary>
    /// Immutable record describing an active authenticated session.
    /// Provides consistent identity information across EOS and third-party storefronts.
    /// </summary>
    [Serializable]
    public struct PlatformAuthInfo
    {
        public string ProductUserId { get; }
        public string EpicAccountId { get; }
        public string DisplayName { get; }
        public PlatformAuthType AuthType { get; }
        public DateTime LoginTimestampUtc { get; }

        public PlatformAuthInfo(string productUserId, string epicAccountId, string displayName, PlatformAuthType authType)
        {
            ProductUserId = productUserId ?? string.Empty;
            EpicAccountId = epicAccountId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            AuthType = authType;
            LoginTimestampUtc = DateTime.UtcNow;
        }

        public static PlatformAuthInfo Empty => new PlatformAuthInfo(string.Empty, string.Empty, string.Empty, PlatformAuthType.None);
    }
}
