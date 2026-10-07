namespace JustAGame.Core.Platform
{
    /// <summary>
    /// Describes the outcome of a remote network start or join operation.
    /// Used by INetworkManager.StartRemote and INetworkManager.JoinRemote.
    /// </summary>
    public enum NetworkStartResult
    {
        /// <summary>Operation completed successfully.</summary>
        Success = 0,

        /// <summary>EOS SDK or underlying transport is not initialized.</summary>
        NotInitialized = 1,

        /// <summary>A network session is already active.</summary>
        AlreadyActive = 2,

        /// <summary>The join code or host address was empty or malformed.</summary>
        InvalidCode = 3,

        /// <summary>Required bridge or manager component is missing from the scene.</summary>
        MissingDependency = 4,

        /// <summary>An unhandled exception occurred during the operation.</summary>
        InternalError = 5,

        /// <summary>The operation timed out before completing.</summary>
        Timeout = 6
    }
}
