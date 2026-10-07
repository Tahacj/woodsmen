using System.Threading.Tasks;

namespace JustAGame.Core.Platform
{
    /// <summary>
    /// High-level network manager contract for external interfaces and game launchers.
    /// Provides both local (LAN/IP) and remote (EOS Relay) session management.
    /// Consumed via GamePlatform.NetworkManager for clean integration.
    /// </summary>
    public interface INetworkManager
    {
        /// <summary>
        /// Starts a local LAN host on the current machine.
        /// </summary>
        void StartLocal();

        /// <summary>
        /// Connects to a local LAN host at the given IP address.
        /// </summary>
        /// <param name="ip">The IP address of the local host; must not be null or empty.</param>
        void ConnectLocal(string ip);

        /// <summary>
        /// Starts hosting a remote session via EOS Relay.
        /// Failure is returned as a result enum, never thrown as an exception.
        /// </summary>
        Task<NetworkStartResult> StartRemote();

        /// <summary>
        /// Joins a remote session via EOS Relay using the host's product user ID code.
        /// Failure is returned as a result enum, never thrown as an exception.
        /// </summary>
        /// <param name="code">The host's EOS Product User ID; must not be null or empty.</param>
        Task<NetworkStartResult> JoinRemote(string code);

        /// <summary>
        /// Returns the current host's joinable code (EOS Product User ID).
        /// Returns empty string if no session is active or EOS is not initialized.
        /// </summary>
        string GetCode();
    }
}
