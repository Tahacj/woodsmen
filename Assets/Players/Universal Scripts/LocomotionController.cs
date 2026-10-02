using Mirror;
using UnityEngine;
using Woodsmen.CameraSystem;
using Woodsmen.Combat;
using Woodsmen.Utilities;

namespace Woodsmen.Players
{
    /// <summary>
    /// Universal locomotion controller for humanoid characters (Lumberjack, Warrior, etc.).
    /// Features:
    /// - Code-driven physics via CharacterController (NO Root Motion).
    /// - Top-down decoupled movement (W is always global North, mouse aiming rotates character).
    /// - Feeds a 2D Freeform BlendTree (MoveX, MoveZ) for seamless 8-directional running.
    /// - Mirror multiplayer optimized: zero extra bandwidth for remote animation syncing.
    /// - Gravity-only with slope adherence (no jumping).
    /// - Code-driven distance-based step effects (particle dust emit & randomized audio, zero GC).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class LocomotionController : NetworkBehaviour
    {
        [Header("Locomotion Settings")]
        [SerializeField] private float moveSpeed = 6.0f;
        [SerializeField] private float acceleration = 25.0f;
        [SerializeField] private float deceleration = 30.0f;
        [SerializeField] private float rotationSpeed = 15.0f;
        [SerializeField] private float aimRotationSpeed = 25.0f;

        [Header("Gravity & Ground Adherence")]
        [SerializeField] private float gravityMultiplier = 2.5f;
        [SerializeField] private float stickToGroundForce = 2.0f;
        [SerializeField] private float terminalVelocity = -50.0f;

        [Header("Animation Dampening")]
        [SerializeField] private float animDampTime = 0.1f;

        [Header("Step Effects (Optional)")]
        [SerializeField] private ParticleSystem stepDustParticles;
        [SerializeField] private AudioSource stepAudioSource;
        [SerializeField] private AudioClip[] stepAudioClips;
        [SerializeField] private float stepDistanceInterval = 1.6f;
        [SerializeField] private float pitchVariationMin = 0.9f;
        [SerializeField] private float pitchVariationMax = 1.1f;
        [SerializeField] private bool showPerfMetrics = false;

        // Cached Animator parameter hashes (Zero string allocations)
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        // Component References
        private CharacterController _characterController;
        private PlayerInputReader _inputReader;
        private Animator _animator;
        private Camera _mainCamera;
        private CharacterHealth _characterHealth;
        private Woodsmen.Combat.Weapons.ICombatController _combatController;
        private WarriorCombat _warriorCombat;

        // Locomotion Runtime State
        private Vector3 _currentPlanarVelocity;
        private float _verticalVelocity;
        private float _stepDistanceAccumulator;

        // Remote interpolation tracking for Mirror
        private Vector3 _lastPosition;

        private void Awake()
        {
            // Defensive component retrieval
            if (!gameObject.TryGetComponent(out _characterController))
            {
                _characterController = gameObject.AddComponent<CharacterController>();
            }

            gameObject.TryGetComponent(out _inputReader);
            gameObject.TryGetComponentInChildren(out _animator);

            if (!gameObject.TryGetComponent(out _characterHealth))
            {
                _characterHealth = gameObject.AddComponent<CharacterHealth>();
            }

            if (!gameObject.TryGetComponent(out Woodsmen.Inventory.PlayerInventory _))
            {
                gameObject.AddComponent<Woodsmen.Inventory.PlayerInventory>();
            }

            // Defensive Failsafe: Ensure primary action component (LumberjackChopping / PlayerCombatController) is active
            if (gameObject.name.Contains("Lumberjack") && !gameObject.TryGetComponent(out LumberjackChopping _))
            {
                gameObject.AddComponent<LumberjackChopping>();
            }
            if (gameObject.name.Contains("Warrior"))
            {
                if (!gameObject.TryGetComponent(out _combatController) && !gameObject.TryGetComponent(out _warriorCombat))
                {
                    _combatController = gameObject.AddComponent<Woodsmen.Combat.Weapons.PlayerCombatController>();
                }
            }
            else
            {
                gameObject.TryGetComponent(out _combatController);
                gameObject.TryGetComponent(out _warriorCombat);
            }

            _mainCamera = Camera.main;
            _lastPosition = transform.position;
        }

        private void Start()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (IsLocallyControlled && CameraManager.Instance != null)
            {
                CameraManager.Instance.SetTarget(transform, true);
            }
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.SetTarget(transform, true);
            }
        }

        private void OnDestroy()
        {
            if (IsLocallyControlled && CameraManager.Instance != null)
            {
                CameraManager.Instance.ClearTarget(transform);
            }
        }

        /// <summary>
        /// True if this is the local networked player, OR if playing offline/testing without a Mirror network session.
        /// Enables drag-and-drop standalone testing in any scene with zero Mirror exceptions.
        /// </summary>
        private bool IsLocallyControlled => isLocalPlayer || (!NetworkClient.active && !NetworkServer.active);

        private void Update()
        {
            // Death Guard: halt locomotion if dead
            if (_characterHealth != null && _characterHealth.IsDead)
            {
                return;
            }

            if (IsLocallyControlled)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                HandleLocalMovement();
                long tMove = sw.ElapsedTicks;

                sw.Restart();
                HandleLocalRotation();
                long tRot = sw.ElapsedTicks;

                sw.Restart();
                UpdateLocalAnimations();
                long tAnim = sw.ElapsedTicks;

                sw.Restart();
                ProcessStepEffects(_currentPlanarVelocity.magnitude);
                long tStep = sw.ElapsedTicks;

                if (showPerfMetrics && _currentPlanarVelocity.sqrMagnitude > 0.01f && Time.frameCount % 60 == 0)
                {
                    double tickToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    Debug.Log($"<color=#00ffff>[PERF Locomotion]</color> CC.Move: <b>{tMove * tickToMs:F3}ms</b> | Rot: <b>{tRot * tickToMs:F3}ms</b> | Anim: <b>{tAnim * tickToMs:F3}ms</b> | Step: <b>{tStep * tickToMs:F3}ms</b> | Frame: <b>{Time.deltaTime * 1000f:F1}ms</b> (<b>{1f / Mathf.Max(0.0001f, Time.deltaTime):F0} FPS</b>)");
                }
            }
            else
            {
                HandleRemoteAnimations();
            }
        }

        #region Local Player Logic

        /// <summary>
        /// Moves the character in world-space coordinates (W = Global North, S = Global South, etc.).
        /// Character facing does not affect world travel direction.
        /// </summary>
        private void HandleLocalMovement()
        {
            Vector2 input = _inputReader != null ? _inputReader.MoveInput : Vector2.zero;
            Vector3 targetDirection = new Vector3(input.x, 0f, input.y);

            if (targetDirection.sqrMagnitude > 1.0f)
            {
                targetDirection.Normalize();
            }

            Vector3 targetPlanarVelocity = targetDirection * moveSpeed;

            // Attack weightiness: scale movement during weapon attacks
            if (_combatController != null && _combatController.IsAttackActive)
            {
                targetPlanarVelocity *= _combatController.AttackMovementMultiplier;
            }
            else if (_warriorCombat != null && _warriorCombat.IsAttackActive)
            {
                targetPlanarVelocity *= _warriorCombat.AttackMovementMultiplier;
            }

            float currentAccelRate = targetDirection.sqrMagnitude > 0.001f ? acceleration : deceleration;

            // Smooth code-driven acceleration (Zero Root Motion)
            _currentPlanarVelocity = Vector3.MoveTowards(
                _currentPlanarVelocity,
                targetPlanarVelocity,
                currentAccelRate * Time.deltaTime
            );

            // Gravity-only calculation with ground snapping
            if (_characterController.isGrounded)
            {
                if (_verticalVelocity < 0f)
                {
                    _verticalVelocity = -stickToGroundForce;
                }
            }
            else
            {
                _verticalVelocity += Physics.gravity.y * gravityMultiplier * Time.deltaTime;
                if (_verticalVelocity < terminalVelocity)
                {
                    _verticalVelocity = terminalVelocity;
                }
            }

            Vector3 totalVelocity = _currentPlanarVelocity + Vector3.up * _verticalVelocity;
            _characterController.Move(totalVelocity * Time.deltaTime);
        }

        /// <summary>
        /// Handles character rotation:
        /// - If aiming (LMB/RMB held): Faces the mouse cursor projected onto the ground plane.
        /// - If free moving: Smoothly rotates toward the movement direction.
        /// </summary>
        private void HandleLocalRotation()
        {
            bool isAiming = _inputReader != null && _inputReader.IsAiming;

            // If pointer is over UI (e.g. inventory slots/buttons), don't aim towards UI clicks
            if (isAiming && Woodsmen.Inventory.UI.UniversalInventoryUI.IsOpen &&
                UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                isAiming = false;
            }

            if (isAiming)
            {
                if (_mainCamera == null) _mainCamera = Camera.main;
                if (_mainCamera != null && _inputReader != null)
                {
                    Ray ray = _mainCamera.ScreenPointToRay(_inputReader.PointerPosition);
                    Plane groundPlane = new Plane(Vector3.up, transform.position);

                    if (groundPlane.Raycast(ray, out float enterDistance))
                    {
                        Vector3 hitPoint = ray.GetPoint(enterDistance);
                        Vector3 lookDirection = hitPoint - transform.position;
                        lookDirection.y = 0f;

                        if (lookDirection.sqrMagnitude > 0.001f)
                        {
                            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
                            transform.rotation = Quaternion.Slerp(
                                transform.rotation,
                                targetRotation,
                                aimRotationSpeed * Time.deltaTime
                            );
                        }
                    }
                }
            }
            else
            {
                // Free running: rotate towards movement direction
                Vector3 moveDir = _currentPlanarVelocity;
                moveDir.y = 0f;

                if (moveDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(moveDir);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        rotationSpeed * Time.deltaTime
                    );
                }
            }
        }

        /// <summary>
        /// Feeds the 2D Blend Tree parameters (MoveX, MoveZ) relative to the character's facing direction.
        /// </summary>
        private void UpdateLocalAnimations()
        {
            if (_animator == null) return;

            // Convert world movement into local-relative space
            Vector3 localVelocity = transform.InverseTransformDirection(_currentPlanarVelocity) / Mathf.Max(0.01f, moveSpeed);

            _animator.SetFloat(MoveXHash, localVelocity.x, animDampTime, Time.deltaTime);
            _animator.SetFloat(MoveZHash, localVelocity.z, animDampTime, Time.deltaTime);
            _animator.SetFloat(SpeedHash, _currentPlanarVelocity.magnitude, animDampTime, Time.deltaTime);
        }

        #endregion

        #region Remote Player Logic (Mirror Zero-Bandwidth Animation Sync)

        /// <summary>
        /// Derives MoveX and MoveZ from the interpolated transform velocity on remote clients.
        /// Consumes 0 additional network bandwidth.
        /// </summary>
        private void HandleRemoteAnimations()
        {
            if (_animator == null) return;

            Vector3 positionDelta = transform.position - _lastPosition;
            _lastPosition = transform.position;

            Vector3 planarVelocity = new Vector3(positionDelta.x, 0f, positionDelta.z) / Mathf.Max(0.0001f, Time.deltaTime);
            Vector3 localVelocity = transform.InverseTransformDirection(planarVelocity) / Mathf.Max(0.01f, moveSpeed);

            _animator.SetFloat(MoveXHash, localVelocity.x, animDampTime, Time.deltaTime);
            _animator.SetFloat(MoveZHash, localVelocity.z, animDampTime, Time.deltaTime);
            _animator.SetFloat(SpeedHash, planarVelocity.magnitude, animDampTime, Time.deltaTime);

            ProcessStepEffects(planarVelocity.magnitude);
        }

        #endregion

        #region Step Effects (Dust & Audio)

        /// <summary>
        /// Code-driven step effects based on accumulated horizontal distance.
        /// Zero GC allocations: emits from pre-instantiated ParticleSystem and plays randomized AudioSource.
        /// </summary>
        private void ProcessStepEffects(float speed)
        {
            if (speed < 0.2f)
            {
                _stepDistanceAccumulator = 0f;
                return;
            }

            _stepDistanceAccumulator += speed * Time.deltaTime;

            if (_stepDistanceAccumulator >= stepDistanceInterval)
            {
                _stepDistanceAccumulator = 0f;
                TriggerStepEffect();
            }
        }

        private void TriggerStepEffect()
        {
            // Optional dust particles
            if (stepDustParticles != null)
            {
                stepDustParticles.Emit(2);
            }

            // Optional footstep audio with pitch variation
            if (stepAudioSource != null && stepAudioClips != null && stepAudioClips.Length > 0)
            {
                int randomIndex = Random.Range(0, stepAudioClips.Length);
                AudioClip clip = stepAudioClips[randomIndex];

                if (clip != null)
                {
                    stepAudioSource.pitch = Random.Range(pitchVariationMin, pitchVariationMax);
                    stepAudioSource.PlayOneShot(clip);
                }
            }
        }

        #endregion
    }
}
