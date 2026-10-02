using Mirror;
using UnityEngine;
using UnityEngine.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Woodsmen.UI;

namespace Woodsmen.Environment
{
    /// <summary>
    /// Network-synchronized sliding gate (left/right shifting).
    /// Features:
    /// - Multi-player network synchronization via Mirror ([SyncVar] & [Command]).
    /// - Supports both double-sliding doors (left & right wings) and single sliding doors.
    /// - Smooth interpolation with interruptible/reversible movement (no popping).
    /// - NavMeshObstacle integration (carving on when closed, carving off when open).
    /// - Proximity detection with "[E] Open Gate" / "[E] Close Gate" UI prompt.
    /// - Input detection compatible with both new Input System and legacy Input Manager.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public class NetworkSlidingGate : NetworkBehaviour
    {
        [Header("Door Wings")]
        [Tooltip("Transform of the left door wing.")]
        [SerializeField] private Transform leftDoor;

        [Tooltip("Transform of the right door wing (optional for single doors).")]
        [SerializeField] private Transform rightDoor;

        [Tooltip("If true, only the left door (or single door) slides.")]
        [SerializeField] private bool singleDoorMode = false;

        [Header("Sliding Motion")]
        [Tooltip("Local direction to slide (default is local X axis: (1, 0, 0)).")]
        [SerializeField] private Vector3 slideAxis = Vector3.right;

        [Tooltip("Distance each door shifts outward when fully opened (in meters).")]
        [SerializeField] private float slideDistance = 2.5f;

        [Tooltip("Time in seconds to fully open or close.")]
        [SerializeField] private float slideDuration = 1.0f;

        [Tooltip("Easing curve for opening and closing.")]
        [SerializeField] private AnimationCurve motionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Player Interaction")]
        [Tooltip("Maximum distance from the gate interaction center to trigger the prompt and press [E].")]
        [SerializeField] private float interactionRadius = 3.5f;

        [Tooltip("Center point for distance check (defaults to this GameObject's transform).")]
        [SerializeField] private Transform interactionCenter;

        [Tooltip("Prompt displayed when gate is closed.")]
        [SerializeField] private string openPrompt = "Open Gate";

        [Tooltip("Prompt displayed when gate is open.")]
        [SerializeField] private string closePrompt = "Close Gate";

        [Header("NavMesh & Colliders")]
        [Tooltip("Optional NavMeshObstacle. Carving is enabled when closed and disabled when open.")]
        [SerializeField] private NavMeshObstacle navMeshObstacle;

        [Header("Audio (Optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip closeSound;

        [SyncVar(hook = nameof(OnOpenStateChanged))]
        private bool isOpen = false;

        public bool IsOpen => isOpen;

        // Cached local positions
        private Vector3 _leftClosedLocalPos;
        private Vector3 _leftOpenLocalPos;
        private Vector3 _rightClosedLocalPos;
        private Vector3 _rightOpenLocalPos;

        private float _currentProgress = 0f; // 0 = fully closed, 1 = fully open
        private bool _isPlayerInRange = false;

        // Optional: sibling or parent ShopZone that should take priority over E when the gate is open.
        private Woodsmen.Shop.ShopZone _linkedShopZone;

        private void Awake()
        {
            if (interactionCenter == null) interactionCenter = transform;
            if (navMeshObstacle == null) navMeshObstacle = GetComponentInChildren<NavMeshObstacle>();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();

            // Cache a sibling ShopZone on any child of the same root prefab,
            // so we can yield the E key to it when the gate is already open.
            _linkedShopZone = transform.root.GetComponentInChildren<Woodsmen.Shop.ShopZone>();

            CacheDoorPositions();
            ApplyNavMeshState(isOpen);
        }

        private void Start()
        {
            // Set initial door positions
            _currentProgress = isOpen ? 1f : 0f;
            UpdateDoorTransforms(_currentProgress);
        }

        private void CacheDoorPositions()
        {
            Vector3 normAxis = slideAxis.normalized;

            if (leftDoor != null)
            {
                _leftClosedLocalPos = leftDoor.localPosition;
                _leftOpenLocalPos = _leftClosedLocalPos - (normAxis * slideDistance);
            }

            if (rightDoor != null && !singleDoorMode)
            {
                _rightClosedLocalPos = rightDoor.localPosition;
                _rightOpenLocalPos = _rightClosedLocalPos + (normAxis * slideDistance);
            }
        }

        private void Update()
        {
            AnimateDoorMotion();
            HandleLocalPlayerInteraction();
        }

        private void OnDisable()
        {
            if (_isPlayerInRange)
            {
                _isPlayerInRange = false;
                InteractionPromptUI.Instance?.Hide();
            }
        }

        #region Motion Animation

        private void AnimateDoorMotion()
        {
            float targetProgress = isOpen ? 1f : 0f;

            if (Mathf.Abs(_currentProgress - targetProgress) > 0.001f)
            {
                float step = (1f / Mathf.Max(0.05f, slideDuration)) * Time.deltaTime;
                _currentProgress = Mathf.MoveTowards(_currentProgress, targetProgress, step);

                float evaluatedT = motionCurve != null ? motionCurve.Evaluate(_currentProgress) : _currentProgress;
                UpdateDoorTransforms(evaluatedT);
            }
            else if (_currentProgress != targetProgress)
            {
                _currentProgress = targetProgress;
                UpdateDoorTransforms(_currentProgress);
            }
        }

        private void UpdateDoorTransforms(float t)
        {
            if (leftDoor != null)
            {
                leftDoor.localPosition = Vector3.LerpUnclamped(_leftClosedLocalPos, _leftOpenLocalPos, t);
            }

            if (rightDoor != null && !singleDoorMode)
            {
                rightDoor.localPosition = Vector3.LerpUnclamped(_rightClosedLocalPos, _rightOpenLocalPos, t);
            }
        }

        #endregion

        #region Interaction & Input

        private void HandleLocalPlayerInteraction()
        {
            Transform playerTrans = GetLocalPlayerTransform();
            if (playerTrans == null)
            {
                if (_isPlayerInRange)
                {
                    _isPlayerInRange = false;
                    InteractionPromptUI.Instance?.Hide();
                }
                return;
            }

            Vector3 center = interactionCenter != null ? interactionCenter.position : transform.position;
            float distSqr = (playerTrans.position - center).sqrMagnitude;
            float maxDistSqr = interactionRadius * interactionRadius;

            if (distSqr <= maxDistSqr)
            {
                // Yield E to the ShopZone ONLY when the gate is open AND the player is
                // actively inside the shop zone. Outside the shop zone the gate handles
                // E normally so the player can still close the gate.
                bool shopIsHandling = isOpen
                                      && _linkedShopZone != null
                                      && _linkedShopZone.IsPlayerInside;

                _isPlayerInRange = true;

                if (!shopIsHandling)
                {
                    string prompt = isOpen ? $"[E] {closePrompt}" : $"[E] {openPrompt}";
                    InteractionPromptUI.Instance?.Show(prompt);

                    if (WasInteractPressed())
                    {
                        ToggleGate();
                    }
                }
                // else: ShopZone owns the prompt and E key while the player is inside
            }
            else if (_isPlayerInRange)
            {
                _isPlayerInRange = false;
                InteractionPromptUI.Instance?.Hide();
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
                // Fallback if legacy input manager is disabled in project settings
            }

            return false;
        }

        private Transform GetLocalPlayerTransform()
        {
            if (NetworkClient.localPlayer != null)
            {
                return NetworkClient.localPlayer.transform;
            }

            // Fallback for singleplayer / offline editor testing
            var playerCombat = FindFirstObjectByType<Combat.Weapons.PlayerCombatController>();
            if (playerCombat != null) return playerCombat.transform;

            var playerInv = FindFirstObjectByType<Inventory.PlayerInventory>();
            if (playerInv != null) return playerInv.transform;

            if (Camera.main != null) return Camera.main.transform;

            return null;
        }

        #endregion

        #region Network Synchronization

        public void ToggleGate()
        {
            if (isServer)
            {
                SetGateState(!isOpen);
            }
            else if (!NetworkClient.active)
            {
                // Offline / editor mode — no network active, toggle locally for testing
                isOpen = !isOpen;
                ApplyNavMeshState(isOpen);
                PlaySound(isOpen);
                string prompt = isOpen ? $"[E] {closePrompt}" : $"[E] {openPrompt}";
                if (_isPlayerInRange) InteractionPromptUI.Instance?.Show(prompt);
            }
            else
            {
                CmdToggleGate();
            }
        }

        [Command(requiresAuthority = false)]
        public void CmdToggleGate(NetworkConnectionToClient sender = null)
        {
            // Server distance validation
            if (sender != null && sender.identity != null)
            {
                Vector3 center = interactionCenter != null ? interactionCenter.position : transform.position;
                float distSqr = (sender.identity.transform.position - center).sqrMagnitude;
                if (distSqr > (interactionRadius + 1.5f) * (interactionRadius + 1.5f))
                {
                    Debug.LogWarning($"[NetworkSlidingGate] Rejected gate toggle: player is too far away ({Mathf.Sqrt(distSqr):F1}m)");
                    return;
                }
            }

            SetGateState(!isOpen);
        }

        [Server]
        public void SetGateState(bool open)
        {
            if (isOpen == open) return;
            isOpen = open;
            Debug.Log($"[NetworkSlidingGate] Gate state changed on server: IsOpen={isOpen}");
        }

        private void OnOpenStateChanged(bool oldVal, bool newVal)
        {
            ApplyNavMeshState(newVal);
            PlaySound(newVal);

            // Update UI prompt text dynamically if player is actively standing near the gate
            if (_isPlayerInRange && InteractionPromptUI.Instance != null)
            {
                string prompt = newVal ? $"[E] {closePrompt}" : $"[E] {openPrompt}";
                InteractionPromptUI.Instance.Show(prompt);
            }
        }

        private void ApplyNavMeshState(bool open)
        {
            if (navMeshObstacle != null)
            {
                // When open: disable carving so enemies and players can navigate through
                // When closed: enable carving so NavMesh agents path around the gate
                navMeshObstacle.carving = !open;
                navMeshObstacle.enabled = !open;
            }
        }

        private void PlaySound(bool open)
        {
            if (audioSource == null) return;

            AudioClip clip = open ? openSound : closeSound;
            if (clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        #endregion

        #region Editor Gizmos

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 center = interactionCenter != null ? interactionCenter.position : transform.position;

            // Interaction radius wireframe
            Gizmos.color = isOpen ? new Color(0.2f, 0.9f, 0.4f, 0.6f) : new Color(1f, 0.7f, 0.1f, 0.6f);
            Gizmos.DrawWireSphere(center, interactionRadius);

            // Door sliding arrows
            Vector3 worldAxis = transform.TransformDirection(slideAxis.normalized);

            if (leftDoor != null)
            {
                Gizmos.color = Color.cyan;
                Vector3 leftStart = leftDoor.position;
                Vector3 leftEnd = leftStart - worldAxis * slideDistance;
                Gizmos.DrawLine(leftStart, leftEnd);
                Gizmos.DrawSphere(leftEnd, 0.15f);
            }

            if (rightDoor != null && !singleDoorMode)
            {
                Gizmos.color = Color.magenta;
                Vector3 rightStart = rightDoor.position;
                Vector3 rightEnd = rightStart + worldAxis * slideDistance;
                Gizmos.DrawLine(rightStart, rightEnd);
                Gizmos.DrawSphere(rightEnd, 0.15f);
            }
        }
#endif

        #endregion
    }
}
