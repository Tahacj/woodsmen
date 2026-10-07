using UnityEngine;
using Mirror;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Woodsmen.UI
{
    /// <summary>
    /// Handles toggling the pause menu during gameplay and managing disconnects.
    /// Does not freeze time, allowing the game world to continue.
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        [Tooltip("The main visual panel of the pause menu (contains the buttons).")]
        [SerializeField] private GameObject panel;

        private void Start()
        {
            if (panel != null)
            {
                // Always ensure it starts hidden when entering the gameplay scene
                panel.SetActive(false);
            }
        }

        private void Update()
        {
            if (WasEscapePressed())
            {
                ToggleMenu();
            }
        }

        private bool WasEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    return true;
                }
            }
            catch
            {
                // Fallback
            }
            return false;
        }

        public void ToggleMenu()
        {
            if (panel != null)
            {
                panel.SetActive(!panel.activeSelf);
            }
        }

        /// <summary>
        /// Call this from the Resume Button's OnClick event.
        /// </summary>
        public void Resume()
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        /// <summary>
        /// Call this from the Quit Button's OnClick event.
        /// </summary>
        public void Quit()
        {
            if (NetworkManager.singleton != null)
            {
                // If we are the Host (running both Server & Client)
                if (NetworkServer.active && NetworkClient.isConnected)
                {
                    NetworkManager.singleton.StopHost();
                }
                // If we are just a connected Client
                else if (NetworkClient.isConnected || NetworkClient.active)
                {
                    NetworkManager.singleton.StopClient();
                }
                // If we are just a dedicated server (edge case)
                else if (NetworkServer.active)
                {
                    NetworkManager.singleton.StopServer();
                }
            }
            
            // Mirror's StopClient() does not fire the OnClientDisconnect event if the offline scene is null.
            // We forcefully return to the main menu immediately to guarantee the UI works.
            UnityEngine.SceneManagement.SceneManager.LoadScene("Main Menu");
        }
    }
}
