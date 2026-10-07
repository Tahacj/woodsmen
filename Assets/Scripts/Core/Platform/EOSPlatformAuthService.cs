using System;
using Epic.OnlineServices;
using EpicTransport;
using JustAGame.Core.Network;
using JustAGame.Pooling;
using UnityEngine;

namespace JustAGame.Core.Platform
{
    /// <summary>
    /// Production implementation of IPlatformAuthService.
    /// Orchestrates EOS Connect, Device ID, Steam WebApi, DevAuthTool, and Epic Account Portal.
    /// </summary>
    public sealed class EOSPlatformAuthService : IPlatformAuthService
    {
        public bool IsLoggedIn => EOSSDKComponent.Initialized && !string.IsNullOrEmpty(EOSSDKComponent.LocalUserProductIdString);
        public bool IsConnecting { get; private set; }

        public string LocalProductUserId => EOSSDKComponent.Initialized ? EOSSDKComponent.LocalUserProductIdString : string.Empty;
        public string LocalEpicAccountId => EOSSDKComponent.LocalUserAccountIdString ?? string.Empty;
        public string DisplayName { get; private set; } = "Player";
        public PlatformAuthType ActiveAuthType { get; private set; } = PlatformAuthType.None;

        public PlatformAuthInfo CurrentAuthInfo => new PlatformAuthInfo(LocalProductUserId, LocalEpicAccountId, DisplayName, ActiveAuthType);

        public event Action<PlatformAuthInfo> OnLoginSuccess;
        public event Action<string> OnLoginFailed;
        public event Action OnLoggedOut;
        public event Action<string> OnAuthStatusChanged;

        public EOSPlatformAuthService()
        {
            try
            {
                EOSSDKComponent.OnLoginSuccess += HandleEosLoginSuccess;
                EOSSDKComponent.OnLoginFailed += HandleEosLoginFailed;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformAuthService] Exception in constructor: {ex.Message}");
            }
        }

        public void LoginWithDeviceId(string displayName = null)
        {
            try
            {
                if (IsLoggedIn)
                {
                    OnAuthStatusChanged?.Invoke("Already logged in.");
                    OnLoginSuccess?.Invoke(CurrentAuthInfo);
                    return;
                }
                else
                {
                    // Proceed with login
                }

                IsConnecting = true;
                ActiveAuthType = PlatformAuthType.DeviceId;
                DisplayName = !string.IsNullOrWhiteSpace(displayName) ? displayName.Trim() : "GuestPlayer";

                OnAuthStatusChanged?.Invoke($"Logging in via Device ID ({DisplayName})...");

                EOSSDKComponent.LoginWithDeviceId(DisplayName);
            }
            catch (Exception ex)
            {
                IsConnecting = false;
                string error = $"Exception in LoginWithDeviceId: {ex.Message}";
                Debug.LogError($"[EOSPlatformAuthService] {error}");
                OnLoginFailed?.Invoke(error);
            }
        }

        public void LoginWithSteam()
        {
            try
            {
                if (IsLoggedIn)
                {
                    OnAuthStatusChanged?.Invoke("Already logged in.");
                    OnLoginSuccess?.Invoke(CurrentAuthInfo);
                    return;
                }
                else
                {
                    // Proceed with login
                }

                if (SteamAuthManager.Instance.IsNull())
                {
                    string error = "SteamAuthManager instance not found in scene.";
                    Debug.LogError($"[EOSPlatformAuthService] {error}");
                    OnLoginFailed?.Invoke(error);
                    return;
                }
                else
                {
                    // Instance found
                }

                IsConnecting = true;
                ActiveAuthType = PlatformAuthType.Steam;
                DisplayName = SteamAuthManager.Instance.SteamPersonaName;

                OnAuthStatusChanged?.Invoke($"Requesting Steam ticket for {DisplayName}...");

                SteamAuthManager.Instance.StartSteamLogin((success, token) =>
                {
                    try
                    {
                        if (!success)
                        {
                            IsConnecting = false;
                            string error = "Steam ticket generation failed.";
                            OnAuthStatusChanged?.Invoke(error);
                            OnLoginFailed?.Invoke(error);
                        }
                        else
                        {
                            OnAuthStatusChanged?.Invoke("Steam ticket acquired. Validating with EOS Connect...");
                        }
                    }
                    catch (Exception cbEx)
                    {
                        Debug.LogError($"[EOSPlatformAuthService] Exception in Steam login callback: {cbEx.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                IsConnecting = false;
                string error = $"Exception in LoginWithSteam: {ex.Message}";
                Debug.LogError($"[EOSPlatformAuthService] {error}");
                OnLoginFailed?.Invoke(error);
            }
        }

        public void LoginWithDevAuth(string serverHostAndPort, string credentialName)
        {
            try
            {
                if (IsLoggedIn)
                {
                    OnAuthStatusChanged?.Invoke("Already logged in.");
                    OnLoginSuccess?.Invoke(CurrentAuthInfo);
                    return;
                }
                else
                {
                    // Proceed with login
                }

                string trimmedServer = string.IsNullOrWhiteSpace(serverHostAndPort) ? "localhost:7878" : serverHostAndPort.Trim();
                string trimmedCred = string.IsNullOrWhiteSpace(credentialName) ? "Player1" : credentialName.Trim();

                string[] parts = trimmedServer.Split(':');
                uint port = 7878;
                if (parts.Length > 1 && uint.TryParse(parts[1], out uint parsedPort))
                {
                    port = parsedPort;
                }
                else
                {
                    // Use default 7878
                }

                IsConnecting = true;
                ActiveAuthType = PlatformAuthType.DevAuthTool;
                DisplayName = trimmedCred;

                OnAuthStatusChanged?.Invoke($"Connecting to DevAuthTool at {trimmedServer} ({trimmedCred})...");

                EOSSDKComponent.LoginWithDevAuth(trimmedCred, port);
            }
            catch (Exception ex)
            {
                IsConnecting = false;
                string error = $"Exception in LoginWithDevAuth: {ex.Message}";
                Debug.LogError($"[EOSPlatformAuthService] {error}");
                OnLoginFailed?.Invoke(error);
            }
        }

        public void LoginWithEpicAccount()
        {
            try
            {
                if (IsLoggedIn)
                {
                    OnAuthStatusChanged?.Invoke("Already logged in.");
                    OnLoginSuccess?.Invoke(CurrentAuthInfo);
                    return;
                }
                else
                {
                    // Proceed with login
                }

                IsConnecting = true;
                ActiveAuthType = PlatformAuthType.EpicAccountPortal;
                DisplayName = "EpicUser";

                OnAuthStatusChanged?.Invoke("Opening Epic Games Account Portal in browser...");

                EOSSDKComponent.LoginWithEpicAccount();
            }
            catch (Exception ex)
            {
                IsConnecting = false;
                string error = $"Exception in LoginWithEpicAccount: {ex.Message}";
                Debug.LogError($"[EOSPlatformAuthService] {error}");
                OnLoginFailed?.Invoke(error);
            }
        }

        public void Logout()
        {
            try
            {
                IsConnecting = false;
                ActiveAuthType = PlatformAuthType.None;
                DisplayName = string.Empty;

                OnAuthStatusChanged?.Invoke("Logged out.");
                OnLoggedOut?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformAuthService] Exception in Logout: {ex.Message}");
            }
        }

        private void HandleEosLoginSuccess()
        {
            try
            {
                IsConnecting = false;
                var info = CurrentAuthInfo;
                OnAuthStatusChanged?.Invoke($"Logged in successfully! PUID: {info.ProductUserId}");
                OnLoginSuccess?.Invoke(info);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformAuthService] Exception in HandleEosLoginSuccess: {ex.Message}");
            }
        }

        private void HandleEosLoginFailed(Result result)
        {
            try
            {
                IsConnecting = false;
                string error = $"Login failed: {result} (Code: {(int)result})";
                OnAuthStatusChanged?.Invoke(error);
                OnLoginFailed?.Invoke(error);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSPlatformAuthService] Exception in HandleEosLoginFailed: {ex.Message}");
            }
        }
    }
}
