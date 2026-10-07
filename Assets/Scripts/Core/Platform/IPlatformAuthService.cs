using System;

namespace JustAGame.Core.Platform
{
    /// <summary>
    /// Service contract for multi-provider player authentication.
    /// Supports Device ID, Steam, DevAuthTool, and Epic Account Portal.
    /// </summary>
    public interface IPlatformAuthService
    {
        bool IsLoggedIn { get; }
        bool IsConnecting { get; }
        string LocalProductUserId { get; }
        string LocalEpicAccountId { get; }
        string DisplayName { get; }
        PlatformAuthType ActiveAuthType { get; }
        PlatformAuthInfo CurrentAuthInfo { get; }

        event Action<PlatformAuthInfo> OnLoginSuccess;
        event Action<string> OnLoginFailed;
        event Action OnLoggedOut;
        event Action<string> OnAuthStatusChanged;

        void LoginWithDeviceId(string displayName = null);
        void LoginWithSteam();
        void LoginWithDevAuth(string serverHostAndPort, string credentialName);
        void LoginWithEpicAccount();
        void Logout();
    }
}
