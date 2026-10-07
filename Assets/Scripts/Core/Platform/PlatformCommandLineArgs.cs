using System;
using System.Collections.Generic;
using JustAGame;

namespace JustAGame.Core.Platform
{
    /// <summary>
    /// Parses and holds CLI arguments supplied by external launchers, CI/CD runners, or orchestrators.
    /// Supports: -autologin, -playername, -devauth-server, -devauth-cred, -autohost, -autojoin, -whitelist, -hideui
    /// </summary>
    public sealed class PlatformCommandLineArgs
    {
        public bool HasAutoLogin { get; private set; }
        public PlatformAuthType AutoLoginType { get; private set; } = PlatformAuthType.None;
        public string PlayerName { get; private set; } = string.Empty;
        public string DevAuthServer { get; private set; } = "localhost:7878";
        public string DevAuthCredential { get; private set; } = "Player1";

        public bool HasAutoHost { get; private set; }
        public bool HasAutoJoin { get; private set; }
        public string TargetHostPuid { get; private set; } = string.Empty;

        public bool HideUI { get; private set; }
        public List<string> WhitelistPuids { get; } = new List<string>();

        public static PlatformCommandLineArgs ParseCurrent()
        {
            var args = new PlatformCommandLineArgs();
            try
            {
                string[] cmdArgs = Environment.GetCommandLineArgs();
                if (cmdArgs.IsNull() || cmdArgs.Length <= 1)
                {
                    return args;
                }
                else
                {
                    // Parse arguments
                }

                for (int i = 1; i < cmdArgs.Length; i++)
                {
                    string arg = cmdArgs[i];
                    if (string.IsNullOrWhiteSpace(arg))
                    {
                        continue;
                    }
                    else
                    {
                        // Valid argument string
                    }

                    string lower = arg.ToLowerInvariant();

                    if (lower == "-autologin" && i + 1 < cmdArgs.Length)
                    {
                        args.HasAutoLogin = true;
                        string val = cmdArgs[++i].ToLowerInvariant();
                        if (val.Contains("steam"))
                        {
                            args.AutoLoginType = PlatformAuthType.Steam;
                        }
                        else if (val.Contains("devauth") || val.Contains("dev"))
                        {
                            args.AutoLoginType = PlatformAuthType.DevAuthTool;
                        }
                        else if (val.Contains("epic") || val.Contains("portal"))
                        {
                            args.AutoLoginType = PlatformAuthType.EpicAccountPortal;
                        }
                        else
                        {
                            args.AutoLoginType = PlatformAuthType.DeviceId;
                        }
                    }
                    else if ((lower == "-playername" || lower == "-displayname") && i + 1 < cmdArgs.Length)
                    {
                        args.PlayerName = cmdArgs[++i].Trim();
                    }
                    else if (lower == "-devauth-server" && i + 1 < cmdArgs.Length)
                    {
                        args.DevAuthServer = cmdArgs[++i].Trim();
                    }
                    else if (lower == "-devauth-cred" && i + 1 < cmdArgs.Length)
                    {
                        args.DevAuthCredential = cmdArgs[++i].Trim();
                    }
                    else if (lower == "-autohost" || lower == "-host")
                    {
                        args.HasAutoHost = true;
                    }
                    else if ((lower == "-autojoin" || lower == "-join") && i + 1 < cmdArgs.Length)
                    {
                        args.HasAutoJoin = true;
                        args.TargetHostPuid = cmdArgs[++i].Trim();
                    }
                    else if (lower == "-whitelist" && i + 1 < cmdArgs.Length)
                    {
                        string list = cmdArgs[++i];
                        string[] parts = list.Split(',', ';');
                        for (int p = 0; p < parts.Length; p++)
                        {
                            string item = parts[p].Trim();
                            if (!string.IsNullOrEmpty(item))
                            {
                                args.WhitelistPuids.Add(item);
                            }
                            else
                            {
                                // Empty item
                            }
                        }
                    }
                    else if (lower == "-hideui" || lower == "-headless" || lower == "-batchmode")
                    {
                        args.HideUI = true;
                    }
                    else
                    {
                        // Unrecognized argument or positional flag
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[PlatformCommandLineArgs] Exception in ParseCurrent: {ex.Message}");
            }

            return args;
        }
    }
}
