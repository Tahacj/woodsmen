using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Woodsmen.CameraSystem;
using Woodsmen.Combat;
using Woodsmen.Environment;
using Woodsmen.Utilities;

namespace Woodsmen.Players
{
    /// <summary>
    /// Handles the Lumberjack's primary action: chopping trees.
    /// Features:
    /// - Driven by PlayerInputReader (PrimaryAction / LMB).
    /// - Sets 'IsCutting' parameter to drive the sequenced dual-swing UpperBody animation layer.
    /// - Fully customizable weapon hitbox (Sphere / Box, offsets, custom weapon transform anchor, or WeaponHitbox component).
    /// - Animation speed multiplier and recovery cooldown pacing (ready for upgrade systems).
    /// - Exposes Animation Event callbacks: EnableTreeHitbox() and DisableTreeHitbox().
    /// - Zero-GC non-allocating physics overlap queries.
    /// - Per-swing multi-hit protection so each tree is struck once per swing.
    /// - Offline testing support and Mirror network compliance.
    /// </summary>
    public class LumberjackChopping : NetworkBehaviour
    {
        [Header("Damage & Action")]
        [Tooltip("Damage dealt per swing against trees and objects.")]
        [SerializeField] private float chopDamage = 25f;

        [Header("Weapon Hitbox Specification")]
        [Tooltip("Optional dedicated WeaponHitbox component. If assigned or found in children, it overrides settings below.")]
        [SerializeField] private WeaponHitbox customWeaponHitbox;

        [Tooltip("Optional transform to anchor the hitbox to (e.g. Axe head bone or Hand). If null, player root is used.")]
        [SerializeField] private Transform weaponTransform;

        [Tooltip("Hitbox geometry: Sphere or Box.")]
        [SerializeField] private HitboxShape hitboxShape = HitboxShape.Sphere;

        [Tooltip("Sphere radius (when HitboxShape is Sphere).")]
        [SerializeField] private float hitboxRadius = 0.8f;

        [Tooltip("Full dimensions (width, height, depth) when HitboxShape is Box.")]
        [SerializeField] private Vector3 hitboxBoxSize = new Vector3(0.8f, 0.8f, 1.2f);

        [Tooltip("Position offset relative to weaponTransform (or player root).")]
        [SerializeField] private Vector3 hitboxCenterOffset = new Vector3(0f, 1.0f, 0.8f);

        [Tooltip("Rotation offset relative to weaponTransform (useful for orienting Box hitboxes).")]
        [SerializeField] private Vector3 hitboxRotationOffset = Vector3.zero;

        [Tooltip("Layer mask for interactable objects.")]
        [SerializeField] private LayerMask targetLayer = ~0;

        [Header("Hitbox Visualizer (Scene View)")]
        [Tooltip("Show live visual hitbox gizmo in Scene view.")]
        [SerializeField] private bool showHitboxGizmos = true;

        [Tooltip("Only show gizmos when this character is selected.")]
        [SerializeField] private bool showGizmosOnlyWhenSelected = false;

        [SerializeField] private Color hitboxInactiveColor = new Color(0f, 0.9f, 0.3f, 0.25f);
        [SerializeField] private Color hitboxActiveColor = new Color(1f, 0.15f, 0.15f, 0.55f);

        [Header("Speed & Cooldown (Upgradable)")]
        [Tooltip("Multiplier for the chopping animation speed. Higher = faster swing.")]
        [SerializeField] private float chopSpeedMultiplier = 1.0f;

        [Tooltip("Cooldown period in seconds after each swing before the next action can begin.")]
        [SerializeField] private float chopCooldown = 0.35f;

        [Header("Audio & Effects")]
        [SerializeField] private AudioSource axeAudioSource;
        [SerializeField] private AudioClip swingAudioClip;
        [SerializeField] private float swingPitchMin = 0.9f;
        [SerializeField] private float swingPitchMax = 1.1f;

        // Cached Animator parameter hashes (Zero string allocations, Rule 5)
        private static readonly int IsCuttingHash = Animator.StringToHash("IsCutting");
        private static readonly int ChopSpeedHash = Animator.StringToHash("ChopSpeed");
        private static readonly int ChopIndexHash = Animator.StringToHash("ChopIndex");

        private Animator _animator;
        private PlayerInputReader _inputReader;

        // Hitbox & Sequencing State
        private bool _isHitboxActive;
        private bool _isChopActive;
        private int _currentChopIndex;
        private float _cooldownTimer;
        private readonly HashSet<int> _hitTreeIdsThisSwing = new HashSet<int>(8);
        private readonly Collider[] _overlapResults = new Collider[16];

        private bool IsLocallyControlled => isLocalPlayer || (!NetworkClient.active && !NetworkServer.active);

        #region Public Upgrade & Inspection API

        /// <summary>
        /// Damage dealt per swing against trees and objects.
        /// Upgradable by future tool/crafting progression.
        /// </summary>
        public float ChopDamage
        {
            get => chopDamage;
            set => chopDamage = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Multiplier for the chopping animation playback speed.
        /// Upgradable by future perk/crafting systems.
        /// </summary>
        public float ChopSpeedMultiplier
        {
            get => chopSpeedMultiplier;
            set
            {
                chopSpeedMultiplier = Mathf.Max(0.1f, value);
                if (_animator != null)
                {
                    _animator.SetFloat(ChopSpeedHash, chopSpeedMultiplier);
                }
            }
        }

        /// <summary>
        /// Cooldown time in seconds between chops.
        /// Upgradable by future perk/crafting systems (smaller = faster recovery).
        /// </summary>
        public float ChopCooldown
        {
            get => chopCooldown;
            set => chopCooldown = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Current remaining cooldown time in seconds.
        /// </summary>
        public float CooldownRemaining => Mathf.Max(0f, _cooldownTimer);

        /// <summary>
        /// Maximum cooldown duration configured.
        /// </summary>
        public float CooldownDuration => chopCooldown;

        /// <summary>
        /// Normalized cooldown progress from 1.0 (just started cooldown) down to 0.0 (ready).
        /// Useful for UI cooldown radial/progress bars.
        /// </summary>
        public float CooldownNormalized => chopCooldown > 0f ? Mathf.Clamp01(_cooldownTimer / chopCooldown) : 0f;

        /// <summary>
        /// True if currently in the cooldown recovery phase between swings.
        /// </summary>
        public bool IsOnCooldown => _cooldownTimer > 0f;

        /// <summary>
        /// True while a swing animation is actively executing.
        /// </summary>
        public bool IsChopActive => _isChopActive;

        public HitboxShape HitboxShape { get => hitboxShape; set => hitboxShape = value; }
        public float HitboxRadius { get => hitboxRadius; set => hitboxRadius = Mathf.Max(0.01f, value); }
        public Vector3 HitboxBoxSize { get => hitboxBoxSize; set => hitboxBoxSize = value; }
        public Vector3 HitboxCenterOffset { get => hitboxCenterOffset; set => hitboxCenterOffset = value; }
        public Vector3 HitboxRotationOffset { get => hitboxRotationOffset; set => hitboxRotationOffset = value; }
        public Transform WeaponTransform { get => weaponTransform; set => weaponTransform = value; }
        public WeaponHitbox CustomWeaponHitbox { get => customWeaponHitbox; set => customWeaponHitbox = value; }
        public LayerMask TargetLayer { get => targetLayer; set => targetLayer = value; }

        #endregion

        private void Awake()
        {
            EnsureDependencies();
        }

        private void EnsureDependencies()
        {
            if (customWeaponHitbox == null && !gameObject.TryGetComponent(out customWeaponHitbox))
            {
                gameObject.TryGetComponentInChildren(out customWeaponHitbox);
            }

            if (_inputReader == null && !gameObject.TryGetComponent(out _inputReader))
            {
                gameObject.TryGetComponentInParent(out _inputReader);
            }

            if (_animator == null && !gameObject.TryGetComponent(out _animator))
            {
                gameObject.TryGetComponentInChildren(out _animator);
            }

            if (_animator != null)
            {
                _animator.SetFloat(ChopSpeedHash, chopSpeedMultiplier);
                _animator.SetInteger(ChopIndexHash, _currentChopIndex);
            }

            if (axeAudioSource == null && !gameObject.TryGetComponent(out axeAudioSource))
            {
                axeAudioSource = gameObject.AddComponent<AudioSource>();
                axeAudioSource.playOnAwake = false;
                axeAudioSource.spatialBlend = 0.5f;
            }
        }

        private void Update()
        {
            if (!IsLocallyControlled) return;

            // Death Guard: do not allow chopping if character is dead
            if (TryGetComponent(out CharacterHealth health) && health.IsDead) return;

            if (_inputReader == null || _animator == null)
            {
                EnsureDependencies();
            }

            // 1. Tick cooldown timer
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
                if (_cooldownTimer < 0f) _cooldownTimer = 0f;
            }

            // 2. Synchronize speed multiplier parameter
            if (_animator != null)
            {
                _animator.SetFloat(ChopSpeedHash, chopSpeedMultiplier);
            }

            // 3. Monitor active swing completion
            if (_isChopActive && _animator != null)
            {
                int cuttingLayerIndex = 1;
                if (_animator.layerCount > cuttingLayerIndex)
                {
                    AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(cuttingLayerIndex);
                    bool isSwingState = stateInfo.IsName("Cut_Horizontal") || stateInfo.IsName("Cut_Downward");
                    if (isSwingState && !_animator.IsInTransition(cuttingLayerIndex))
                    {
                        if (stateInfo.normalizedTime >= 0.88f)
                        {
                            EndChopAction();
                        }
                    }
                }
            }

            // 4. Initiate new chop if requested and eligible
            bool isInputHeld = _inputReader != null && _inputReader.IsPrimaryActionHeld;

            if (isInputHeld && !_isChopActive && _cooldownTimer <= 0f)
            {
                StartChopAction();
            }

            // 5. Scan hitbox while active
            if (_isHitboxActive)
            {
                DetectTreesInHitbox();
            }
        }

        private void StartChopAction()
        {
            _isChopActive = true;
            _hitTreeIdsThisSwing.Clear();

            if (_animator != null)
            {
                _animator.SetInteger(ChopIndexHash, _currentChopIndex);
                _animator.SetBool(IsCuttingHash, true);
            }
        }

        private void EndChopAction()
        {
            if (!_isChopActive) return;

            _isChopActive = false;
            _isHitboxActive = false;
            _hitTreeIdsThisSwing.Clear();

            // Start cooldown period
            _cooldownTimer = chopCooldown;

            // Toggle swing animation index for next action (Horizontal <-> Downward)
            _currentChopIndex = 1 - _currentChopIndex;

            if (_animator != null)
            {
                _animator.SetBool(IsCuttingHash, false);
                _animator.SetInteger(ChopIndexHash, _currentChopIndex);
            }
        }

        private void OnDisable()
        {
            // Safety reset on disable/interrupt
            DisableTreeHitbox();
            _isChopActive = false;
            _cooldownTimer = 0f;
            _hitTreeIdsThisSwing.Clear();

            if (_animator != null)
            {
                _animator.SetBool(IsCuttingHash, false);
            }
        }

        #region Animation Event Callbacks

        /// <summary>
        /// Animation Event 1: Called from the cutting animation clip when the active swing phase begins.
        /// </summary>
        public void EnableTreeHitbox()
        {
            _isHitboxActive = true;

            if (customWeaponHitbox != null)
            {
                customWeaponHitbox.SetActive(true);
            }

            // Play whoosh sound
            if (axeAudioSource != null && swingAudioClip != null)
            {
                axeAudioSource.pitch = Random.Range(swingPitchMin, swingPitchMax);
                axeAudioSource.PlayOneShot(swingAudioClip);
            }

            // Immediately run a scan on the first frame of activation
            DetectTreesInHitbox();
        }

        /// <summary>
        /// Animation Event 2: Called from the cutting animation clip when the active swing phase ends.
        /// </summary>
        public void DisableTreeHitbox()
        {
            _isHitboxActive = false;

            if (customWeaponHitbox != null)
            {
                customWeaponHitbox.SetActive(false);
            }
        }

        #endregion

        #region Hit Detection (Zero GC - Rule 5)

        private void DetectTreesInHitbox()
        {
            int hitCount = 0;
            Vector3 hitCenter;

            if (customWeaponHitbox != null)
            {
                hitCount = customWeaponHitbox.CheckOverlap(_overlapResults);
                hitCenter = customWeaponHitbox.WorldCenter;
            }
            else
            {
                Transform origin = weaponTransform != null ? weaponTransform : transform;
                hitCenter = origin.TransformPoint(hitboxCenterOffset);
                Quaternion hitRotation = origin.rotation * Quaternion.Euler(hitboxRotationOffset);

                if (hitboxShape == HitboxShape.Sphere)
                {
                    hitCount = Physics.OverlapSphereNonAlloc(hitCenter, hitboxRadius, _overlapResults, targetLayer);
                }
                else
                {
                    hitCount = Physics.OverlapBoxNonAlloc(hitCenter, hitboxBoxSize * 0.5f, _overlapResults, hitRotation, targetLayer);
                }
            }

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _overlapResults[i];
                if (col == null || col.gameObject == gameObject) continue;

                if (col.TryGetComponentInParent(out IDamageable target))
                {
                    // Avoid self-damage
                    if (target is Component comp && (comp.gameObject == gameObject || comp.transform.IsChildOf(transform)))
                    {
                        continue;
                    }

                    int targetId = (target as Component)?.GetInstanceID() ?? col.GetInstanceID();

                    // Strictly prevent striking the same target instance more than once during a single swing,
                    // regardless of multiple colliders (LODs, compound colliders) or multi-frame queries.
                    if (_hitTreeIdsThisSwing.Contains(targetId)) continue;

                    _hitTreeIdsThisSwing.Add(targetId);

                    Vector3 contactPoint = col.ClosestPoint(hitCenter);
                    Vector3 hitDirection = (col.transform.position - transform.position).normalized;

                    target.TakeDamage(chopDamage, contactPoint, hitDirection, gameObject);

                    Debug.Log($"[Woodsmen] Lumberjack struck target: {(target as Component)?.gameObject.name}");
                }
            }
        }

        #endregion

        #region Debug Gizmos

        private void OnDrawGizmos()
        {
            if (!showHitboxGizmos || showGizmosOnlyWhenSelected) return;
            DrawHitboxGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (!showHitboxGizmos || !showGizmosOnlyWhenSelected) return;
            DrawHitboxGizmo();
        }

        private void DrawHitboxGizmo()
        {
            // If custom WeaponHitbox component is attached, it handles its own gizmo drawing
            if (customWeaponHitbox != null) return;

            Transform origin = weaponTransform != null ? weaponTransform : transform;
            Vector3 center = origin.TransformPoint(hitboxCenterOffset);
            Quaternion rotation = origin.rotation * Quaternion.Euler(hitboxRotationOffset);

            Color fillColor = _isHitboxActive ? hitboxActiveColor : hitboxInactiveColor;
            Color wireColor = new Color(fillColor.r, fillColor.g, fillColor.b, 0.95f);

            if (hitboxShape == HitboxShape.Sphere)
            {
                Gizmos.color = fillColor;
                Gizmos.DrawSphere(center, hitboxRadius);
                Gizmos.color = wireColor;
                Gizmos.DrawWireSphere(center, hitboxRadius);
            }
            else
            {
                Matrix4x4 previousMatrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);

                Gizmos.color = fillColor;
                Gizmos.DrawCube(Vector3.zero, hitboxBoxSize);
                Gizmos.color = wireColor;
                Gizmos.DrawWireCube(Vector3.zero, hitboxBoxSize);

                Gizmos.matrix = previousMatrix;
            }
        }

        #endregion
    }
}
