using System;
using UnityEngine;
using Mirror;
using Woodsmen.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Woodsmen.Combat
{
    /// <summary>
    /// Allows a living local player to revive a dead player by pressing E.
    /// Integrates seamlessly with CharacterHealth.cs and InteractionPromptUI.cs.
    /// Enemies will automatically retarget the revived player thanks to the GoblinAI scanning logic.
    /// </summary>
    [RequireComponent(typeof(CharacterHealth))]
    public class PlayerReviveInteraction : NetworkBehaviour
    {
        [Header("Revive Settings")]
        [Tooltip("How close you need to be to revive a dead player.")]
        [SerializeField] private float reviveRange = 3.5f;
        [Tooltip("The percentage of MaxHealth the player will revive with (e.g., 0.15 for 15%).")]
        [SerializeField] private float reviveHealthPercent = 0.15f;

        private CharacterHealth _myHealth;
        private CharacterHealth _targetDeadPlayer;
        private bool _isPromptShowing;

        private void Awake()
        {
            _myHealth = GetComponent<CharacterHealth>();
        }

        private void Update()
        {
            // Only the local player calculates interactions for themselves
            if (!isLocalPlayer) return;

            // If we are dead, we can't revive anyone
            if (_myHealth.IsDead)
            {
                if (_isPromptShowing)
                {
                    _isPromptShowing = false;
                    InteractionPromptUI.Instance?.Hide();
                }
                return;
            }

            FindDeadPlayerToRevive();

            if (_targetDeadPlayer != null)
            {
                if (!_isPromptShowing)
                {
                    // Clean up the name so "Lumberjack(Clone)" just shows "Lumberjack"
                    string targetName = _targetDeadPlayer.gameObject.name.Replace("(Clone)", "");
                    InteractionPromptUI.Instance?.Show($"[E] Revive {targetName}");
                    _isPromptShowing = true;
                }

                if (WasInteractPressed())
                {
                    CmdRevivePlayer(_targetDeadPlayer.gameObject, reviveHealthPercent);
                    _isPromptShowing = false;
                    InteractionPromptUI.Instance?.Hide();
                    _targetDeadPlayer = null; // Clear immediately so we don't spam commands
                }
            }
            else if (_isPromptShowing)
            {
                _isPromptShowing = false;
                InteractionPromptUI.Instance?.Hide();
            }
        }

        /// <summary>
        /// Scans for the closest dead player in range.
        /// </summary>
        private void FindDeadPlayerToRevive()
        {
            _targetDeadPlayer = null;
            float closestDist = reviveRange * reviveRange;

            // FindObjectsSortMode.None is used for performance over finding specifically
            var allHealths = FindObjectsByType<CharacterHealth>(FindObjectsSortMode.None);
            
            foreach (var h in allHealths)
            {
                if (h == _myHealth) continue; // Can't revive yourself
                if (!h.IsDead) continue;      // Only dead characters

                // Ensure it's a Player, not a dead Goblin/Tree (using tag or common names)
                bool isPlayer = h.gameObject.CompareTag("Player") 
                                || h.gameObject.name.Contains("Lumberjack") 
                                || h.gameObject.name.Contains("Warrior");
                if (!isPlayer) continue;

                float distSqr = (h.transform.position - transform.position).sqrMagnitude;
                if (distSqr < closestDist)
                {
                    closestDist = distSqr;
                    _targetDeadPlayer = h;
                }
            }
        }

        [Command]
        private void CmdRevivePlayer(GameObject targetPlayerObj, float percent)
        {
            if (targetPlayerObj == null) return;
            
            var health = targetPlayerObj.GetComponent<CharacterHealth>();
            // Verify they are still dead on the server before reviving
            if (health != null && health.IsDead)
            {
                health.Revive(percent);
            }
        }

        private bool WasInteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
            try
            {
                if (Input.GetKeyDown(KeyCode.E))
                {
                    return true;
                }
            }
            catch
            {
                // Fallback for missing input manager
            }

            return false;
        }

        private void OnDisable()
        {
            if (_isPromptShowing)
            {
                _isPromptShowing = false;
                InteractionPromptUI.Instance?.Hide();
            }
        }
    }
}
