using System;
using System.Collections.Generic;

namespace JustAGame.Core.Platform
{
    /// <summary>
    /// Service contract for P2P multiplayer session management.
    /// Abstracts Mirror networking and Epic Online Services P2P transport.
    /// </summary>
    public interface IPlatformNetworkService
    {
        bool IsHost { get; }
        bool IsClient { get; }
        bool IsSessionActive { get; }
        string HostAddress { get; }
        int ConnectedPeerCount { get; }
        bool UseActiveWhitelist { get; set; }

        event Action<string> OnHostStarted;
        event Action<string> OnClientStarted;
        event Action OnClientConnected;
        event Action<string> OnClientDisconnected;
        event Action OnSessionStopped;
        event Action<string> OnNetworkError;

        bool StartHost();
        bool JoinHost(string hostProductUserId);
        void StopSession();
        void AddAuthorizedPeer(string productUserId);
        void RemoveAuthorizedPeer(string productUserId);
        bool IsPeerAuthorized(string productUserId);
        void ClearAuthorizedPeers();
        bool CopyHostAddressToClipboard();
    }
}
