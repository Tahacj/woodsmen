using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Woodsmen.CameraSystem;
using Woodsmen.Combat;
using Woodsmen.Environment;
using Woodsmen.Feedback;
using Woodsmen.Utilities;

namespace Woodsmen.Players
{
    /// <summary>
    /// Professional, networked melee combat system for the Warrior class.
    /// Features:
    /// - Driven by PlayerInputReader (PrimaryAction / LMB / Gamepad).
    /// - 3-Hit attack combo sequence driven by UpperBody Attack Layer (Attack_1, Attack_2, Attack_3).
    /// - Synchronizes 'IsAttacking', 'ComboIndex', and 'AttackSpeed' animator parameters.
    /// - Animation Event callbacks: AttackStart(), AttackEnd(), ComboWindow() with failsafe recovery.
    /// - Mirror multiplayer compliance: server-authoritative health interaction, low-bandwidth RPC animation dispatches,
    ///   and 100% offline single-player testing capability (Directive 12).
    /// - Non-allocating physics overlap queries (Zero GC, Rule 5).
    /// - Per-swing target deduplication (each enemy/object struck at most once per swing).
    /// - Upgradable damage, attack speed multiplier, cooldowns, and customizable hitboxes.
    /// - Zero-GC pooled slash particle VFX and dynamic audio with pitch variation.
    /// </summary>
    public class WarriorCombat : NetworkBehaviour, Woodsmen.Combat.Weapons.ICombatController
    {
        public Woodsmen.Combat.Weapons.IWeapon CurrentWeapon => null;

        [Header("Damage & Combat")]
        [Tooltip("Base damage dealt per hit before combo multiplier scaling.")]
        [SerializeField] private float baseAttackDamage = 35f;

        [Tooltip("Damage multipliers for each combo step (Step 0, Step 1, Step 2).")]
        [SerializeField] private float[] comboDamageMultipliers = new float[] { 1.0f, 1.25f, 1.75f };

        [Header("Attack Speed & Pacing")]
        [Tooltip("Animation speed multiplier for attack swings (higher = faster swing).")]
        [SerializeField] private float attackSpeedMultiplier = 1.0f;

        [Tooltip("Cooldown period in seconds between combo steps.")]
        [SerializeField] private float attackCooldown = 0.35f;

        [Tooltip("Recovery cooldown period in seconds after completing the final combo finisher (Attack_3).")]
        [SerializeField] private float finisherRecoveryCooldown = 0.5f;

        [Tooltip("Time in seconds before the combo chain resets back to Step 0 if no follow-up attack is triggered.")]
        [SerializeField] private float comboResetTimeout = 1.2f;

        [Tooltip("Movement speed multiplier applied to LocomotionController during an active attack swing.")]
        [Range(0f, 1f)]
        [SerializeField] private float attackMovementMultiplier = 0.4f;

        [Tooltip("Whether input can buffer during recovery to immediately chain the next combo strike.")]
        [SerializeField] private bool cancelRecoveryOnComboInput = true;

        [Tooltip("If true, attacking continuously will loop the combo endlessly without pause.")]
        [SerializeField] private bool loopComboContinuously = false;

        [Header("Weapon Hitbox Specification")]
        [Tooltip("Optional dedicated WeaponHitbox component. If assigned or found in children, it overrides settings below.")]
        [SerializeField] private WeaponHitbox customWeaponHitbox;

        [Tooltip("Optional transform to anchor the hitbox to (e.g. jointItemR / hand bone). If null, player root is used.")]
        [SerializeField] private Transform weaponTransform;

        [Tooltip("Hitbox geometry: Box or Sphere.")]
        [SerializeField] private HitboxShape hitboxShape = HitboxShape.Box;

        [Tooltip("Sphere radius (when HitboxShape is Sphere).")]
        [SerializeField] private float hitboxRadius = 1.1f;

        [Tooltip("Full dimensions (width, height, depth) when HitboxShape is Box.")]
        [SerializeField] private Vector3 hitboxBoxSize = new Vector3(1.4f, 1.0f, 1.8f);

        [Tooltip("Position offset relative to weaponTransform (or player root).")]
        [SerializeField] private Vector3 hitboxCenterOffset = new Vector3(0f, 0f, 0.8f);

        [Tooltip("Rotation offset relative to weaponTransform (useful for orienting Box hitboxes).")]
        [SerializeField] private Vector3 hitboxRotationOffset = Vector3.zero;

        [Tooltip("Layer mask for interactable objects and damageable targets.")]
        [SerializeField] private LayerMask targetLayer = ~0;

        [Header("Hitbox Visualizer (Scene View)")]
        [Tooltip("Show live visual hitbox gizmo in Scene view.")]
        [SerializeField] private bool showHitboxGizmos = true;

        [Tooltip("Only show gizmos when this character is selected.")]
        [SerializeField] private bool showGizmosOnlyWhenSelected = false;

        [SerializeField] private Color hitboxInactiveColor = new Color(0f, 0.85f, 1f, 0.25f);
        [SerializeField] private Color hitboxActiveColor = new Color(1f, 0.1f, 0.1f, 0.6f);

        [Header("Audio & Effects")]
        [SerializeField] private AudioSource combatAudioSource;
        [SerializeField] private AudioClip[] swingAudioClips;
        [SerializeField] private AudioClip[] hitAudioClips;
        [SerializeField] private float swingPitchMin = 0.9f;
        [SerializeField] private float swingPitchMax = 1.15f;

        [Tooltip("Visual slash particle effect prefab (e.g. FX_SwordSlash_01).")]
        [SerializeField] private GameObject slashVFX;

        // Cached Animator parameter hashes (Zero string allocations, Rule 5)
        private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");
        private static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");
        private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");

        // Component References
        private Animator _animator;
        private PlayerInputReader _inputReader;
        private CharacterHealth _characterHealth;

        // Combat & Combo Runtime State
        private bool _isAttackActive;
        private bool _isHitboxActive;
        private int _currentComboIndex;
        private float _cooldownTimer;
        private float _activeCooldownDuration;
        private float _comboResetTimer;

        // Zero-GC Overlap & Deduplication Buffers (Rule 5)
        private readonly HashSet<int> _hitTargetIdsThisSwing = new HashSet<int>(16);
        private readonly Collider[] _overlapResults = new Collider[16];

        // Slash VFX Pooling (Rule 11)
        private GameObject[] _pooledSlashVfx;
        private int _slashPoolIndex;
        private Transform _vfxPoolContainer;

        /// <summary>
        /// True if this is the locally controlled player or running in single-player offline mode (Directive 12).
        /// </summary>
        private bool IsLocallyControlled => isLocalPlayer || (!NetworkClient.active && !NetworkServer.active);

        #region Public Upgrade & Inspection API

        /// <summary>
        /// Base damage dealt per hit before combo multiplier scaling.
        /// </summary>
        public float BaseAttackDamage
        {
            get => baseAttackDamage;
            set => baseAttackDamage = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Multiplier for the attack animation playback speed.
        /// </summary>
        public float AttackSpeedMultiplier
        {
            get => attackSpeedMultiplier;
            set
            {
                attackSpeedMultiplier = Mathf.Max(0.1f, value);
                if (_animator != null)
                {
                    _animator.SetFloat(AttackSpeedHash, attackSpeedMultiplier);
                }
            }
        }

        /// <summary>
        /// Cooldown time in seconds between combo steps.
        /// </summary>
        public float AttackCooldown
        {
            get => attackCooldown;
            set => attackCooldown = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Recovery cooldown period in seconds after completing the finisher.
        /// </summary>
        public float FinisherRecoveryCooldown
        {
            get => finisherRecoveryCooldown;
            set => finisherRecoveryCooldown = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Movement multiplier applied during an active attack swing.
        /// </summary>
        public float AttackMovementMultiplier => attackMovementMultiplier;

        /// <summary>
        /// Current active step in the combo sequence (0 = Attack_1, 1 = Attack_2, 2 = Attack_3).
        /// </summary>
        public int CurrentComboIndex => _currentComboIndex;

        /// <summary>
        /// Total steps in the configured combo chain.
        /// </summary>
        public int TotalComboSteps => comboDamageMultipliers != null && comboDamageMultipliers.Length > 0 ? comboDamageMultipliers.Length : 3;

        /// <summary>
        /// True while an attack swing is actively executing.
        /// </summary>
        public bool IsAttackActive => _isAttackActive;

        /// <summary>
        /// True while the weapon hitbox is currently active and scanning for targets.
        /// </summary>
        public bool IsHitboxActive => _isHitboxActive;

        /// <summary>
        /// True if currently in cooldown recovery between swings.
        /// </summary>
        public bool IsOnCooldown => _cooldownTimer > 0f;

        /// <summary>
        /// Current remaining cooldown time in seconds.
        /// </summary>
        public float CooldownRemaining => Mathf.Max(0f, _cooldownTimer);

        /// <summary>
        /// Normalized cooldown progress from 1.0 (just started cooldown) down to 0.0 (ready).
        /// </summary>
        public float CooldownNormalized => _activeCooldownDuration > 0f ? Mathf.Clamp01(_cooldownTimer / _activeCooldownDuration) : 0f;

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
            InitializeVfxPool();
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

            if (_characterHealth == null)
            {
                gameObject.TryGetComponent(out _characterHealth);
            }

            // Locate right-hand socket bone if not explicitly assigned
            if (weaponTransform == null)
            {
                Transform socket = transform.Find("jointItemR") ?? transform.Find("Armature/Hips/Spine/Spine1/Spine2/RightShoulder/RightArm/RightForeArm/RightHand/jointItemR");
                if (socket != null) weaponTransform = socket;
            }

            if (_animator != null)
            {
                _animator.SetFloat(AttackSpeedHash, Mathf.Max(0.1f, attackSpeedMultiplier));
                _animator.SetInteger(ComboIndexHash, _currentComboIndex);
            }

            if (combatAudioSource == null && !gameObject.TryGetComponent(out combatAudioSource))
            {
                combatAudioSource = gameObject.AddComponent<AudioSource>();
                combatAudioSource.playOnAwake = false;
                combatAudioSource.spatialBlend = 0.5f;
            }
        }

        private void InitializeVfxPool()
        {
            if (slashVFX == null) return;

            const int poolSize = 4;
            _pooledSlashVfx = new GameObject[poolSize];
            GameObject container = new GameObject("SlashVFX_Pool");
            _vfxPoolContainer = container.transform;
            _vfxPoolContainer.SetParent(transform);

            for (int i = 0; i < poolSize; i++)
            {
                GameObject instance = Instantiate(slashVFX, _vfxPoolContainer);
                instance.name = $"{slashVFX.name}_Pooled_{i}";
                instance.SetActive(false);
                _pooledSlashVfx[i] = instance;
            }

            _slashPoolIndex = 0;
        }

        private void Update()
        {
            if (!IsLocallyControlled) return;

            // Death Guard: do not allow attacks if dead
            if (_characterHealth != null && _characterHealth.IsDead) return;

            if (_inputReader == null || _animator == null)
            {
                EnsureDependencies();
            }

            // 1. Tick cooldown recovery timer
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
                if (_cooldownTimer < 0f) _cooldownTimer = 0f;
            }

            // 2. Tick combo reset timer (resets combo to Step 0 if idle too long)
            if (!_isAttackActive && _currentComboIndex > 0)
            {
                _comboResetTimer -= Time.deltaTime;
                if (_comboResetTimer <= 0f)
                {
                    ResetCombo();
                }
            }

            // 3. Keep attack speed parameter synchronized
            if (_animator != null)
            {
                _animator.SetFloat(AttackSpeedHash, Mathf.Max(0.1f, attackSpeedMultiplier));
            }

            // 4. Monitor active attack swing completion on Layer 1 (Attack Layer)
            if (_isAttackActive && _animator != null)
            {
                int attackLayerIndex = 1;
                if (_animator.layerCount > attackLayerIndex)
                {
                    AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(attackLayerIndex);
                    bool isAttackState = stateInfo.IsName("Attack_1") || stateInfo.IsName("Attack_2") || stateInfo.IsName("Attack_3");

                    if (isAttackState && !_animator.IsInTransition(attackLayerIndex))
                    {
                        if (stateInfo.normalizedTime >= 0.88f)
                        {
                            EndAttackAction();
                        }
                    }
                }
            }

            // 5. Check input for starting/chaining attack
            bool isInputHeld = _inputReader != null && _inputReader.IsPrimaryActionHeld;

            // Guard against clicking on UI elements
            if (isInputHeld && UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                isInputHeld = false;
            }

            if (isInputHeld && !_isAttackActive && _cooldownTimer <= 0f)
            {
                StartAttackAction();
            }

            // 6. Scan hitbox continuously while active
            if (_isHitboxActive)
            {
                DetectTargetsInHitbox();
            }
        }

        #region Attack Execution & Sequencing

        private void StartAttackAction()
        {
            _isAttackActive = true;
            _hitTargetIdsThisSwing.Clear();

            if (_animator != null)
            {
                _animator.SetInteger(ComboIndexHash, _currentComboIndex);
                _animator.SetBool(IsAttackingHash, true);
            }

            // Mirror multiplayer synchronization: dispatch attack event to other clients
            if (NetworkClient.active && !isServer)
            {
                CmdStartAttack(_currentComboIndex);
            }
            else if (isServer && NetworkServer.active)
            {
                RpcOnAttack(_currentComboIndex);
            }
        }

        private void EndAttackAction()
        {
            if (!_isAttackActive) return;

            _isAttackActive = false;
            _isHitboxActive = false;
            _hitTargetIdsThisSwing.Clear();

            if (customWeaponHitbox != null)
            {
                customWeaponHitbox.SetActive(false);
            }

            int steps = TotalComboSteps;
            bool isFinisher = _currentComboIndex >= steps - 1;

            // Apply appropriate recovery cooldown
            _activeCooldownDuration = isFinisher ? finisherRecoveryCooldown : attackCooldown;
            _cooldownTimer = _activeCooldownDuration;

            // Advance combo index
            if (isFinisher)
            {
                _currentComboIndex = 0;
                _comboResetTimer = 0f;
            }
            else
            {
                _currentComboIndex++;
                _comboResetTimer = comboResetTimeout;
            }

            if (_animator != null)
            {
                _animator.SetBool(IsAttackingHash, false);
                _animator.SetInteger(ComboIndexHash, _currentComboIndex);
            }

            // Mirror multiplayer synchronization: notify remote clients attack ended
            if (NetworkClient.active && !isServer)
            {
                CmdEndAttack();
            }
            else if (isServer && NetworkServer.active)
            {
                RpcOnEndAttack();
            }
        }

        public void ResetCombo()
        {
            _currentComboIndex = 0;
            _comboResetTimer = 0f;
            if (_animator != null)
            {
                _animator.SetInteger(ComboIndexHash, 0);
            }
        }

        private void OnDisable()
        {
            // Safety state cleanup on disable or interruption
            DisableAttackHitboxInternal();
            _isAttackActive = false;
            _cooldownTimer = 0f;
            _currentComboIndex = 0;
            _hitTargetIdsThisSwing.Clear();

            if (_animator != null)
            {
                _animator.SetBool(IsAttackingHash, false);
                _animator.SetInteger(ComboIndexHash, 0);
            }
        }

        #endregion

        #region Animation Event Callbacks (Imitating Lumberjack Pattern)

        /// <summary>
        /// Animation Event: Called at the exact impact/active swing frame (FBX event: AttackStart).
        /// </summary>
        public void AttackStart()
        {
            EnableAttackHitboxInternal();
        }

        /// <summary>
        /// Animation Event: Called when the active damaging phase ends (FBX event: AttackEnd).
        /// </summary>
        public void AttackEnd()
        {
            DisableAttackHitboxInternal();
        }

        /// <summary>
        /// Animation Event: Called during the combo window where subsequent inputs can be buffered (FBX event: ComboWindow).
        /// </summary>
        public void ComboWindow()
        {
            // Pacing window for future combo branching/canceling mechanics
        }

        // Aliases and failsafe signatures for animation event compatibility
        public void attack_start() => AttackStart();
        public void attack_end() => AttackEnd();
        public void EnableAttackHitbox() => AttackStart();
        public void DisableAttackHitbox() => AttackEnd();
        public void EnableTreeHitbox() => AttackStart();
        public void DisableTreeHitbox() => AttackEnd();

        private void EnableAttackHitboxInternal()
        {
            _isHitboxActive = true;

            if (customWeaponHitbox != null)
            {
                customWeaponHitbox.SetActive(true);
            }

            // Play randomized swing audio whoosh
            PlaySwingAudio();

            // Play pooled slash particle effect
            PlaySlashVFX();

            // Immediate initial collision check on the first active frame
            DetectTargetsInHitbox();
        }

        private void DisableAttackHitboxInternal()
        {
            _isHitboxActive = false;

            if (customWeaponHitbox != null)
            {
                customWeaponHitbox.SetActive(false);
            }
        }

        #endregion

        #region Hit Detection & Damage (Zero GC - Rule 5)

        private void DetectTargetsInHitbox()
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

            // Calculate scaled combo damage
            float multiplier = 1.0f;
            if (comboDamageMultipliers != null && comboDamageMultipliers.Length > 0)
            {
                int index = Mathf.Clamp(_currentComboIndex, 0, comboDamageMultipliers.Length - 1);
                multiplier = comboDamageMultipliers[index];
            }
            float totalDamage = baseAttackDamage * multiplier;

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _overlapResults[i];
                if (col == null || col.gameObject == gameObject) continue;

                if (col.TryGetComponentInParent(out IDamageable target))
                {
                    // Prevent self-damage
                    if (target is Component comp && (comp.gameObject == gameObject || comp.transform.IsChildOf(transform)))
                    {
                        continue;
                    }

                    int targetId = (target as Component)?.GetInstanceID() ?? col.GetInstanceID();

                    // Strictly prevent striking the same target multiple times within a single swing
                    if (_hitTargetIdsThisSwing.Contains(targetId)) continue;
                    _hitTargetIdsThisSwing.Add(targetId);

                    Vector3 contactPoint = col.ClosestPoint(hitCenter);
                    Vector3 hitDirection = (col.transform.position - transform.position).normalized;

                    // Unified damage routing (Directive 12)
                    target.TakeDamage(totalDamage, contactPoint, hitDirection, gameObject);

                    // Apply active weapon infusion effects (Fire DoT / Frost Slow / Bonus Dmg)
                    var infusion = GetComponentInChildren<Woodsmen.Combat.Weapons.WeaponInfusion>() ??
                                   GetComponent<Woodsmen.Combat.Weapons.WeaponInfusion>();
                    if (infusion != null)
                    {
                        infusion.OnWeaponHit(target, contactPoint, hitDirection, gameObject);
                    }

                    // Impact Audio
                    PlayHitAudio();

                    // If striking a destructible tree, trigger pooled wood splinters
                    if (target is DestructibleTree)
                    {
                        VFXManager.Instance?.PlayWoodImpact(contactPoint, -hitDirection);
                    }

                    // Screen shake feedback for local attacker
                    if (isLocalPlayer || (!NetworkClient.active && !NetworkServer.active))
                    {
                        CameraManager.Instance?.ShakePlayerDamaged();
                    }

                    Debug.Log($"<color=#38bdf8>[Woodsmen Combat]</color> Warrior struck <b>{(target as Component)?.gameObject.name}</b> for <b>{totalDamage:F1}</b> dmg (Combo Step {_currentComboIndex}).");
                }
            }
        }

        #endregion

        #region Feedback & Audio Effects

        private void PlaySwingAudio()
        {
            if (combatAudioSource == null) return;

            AudioClip clip = null;
            if (swingAudioClips != null && swingAudioClips.Length > 0)
            {
                clip = swingAudioClips[Random.Range(0, swingAudioClips.Length)];
            }

            if (clip != null)
            {
                combatAudioSource.pitch = Random.Range(swingPitchMin, swingPitchMax);
                combatAudioSource.PlayOneShot(clip);
            }
        }

        private void PlayHitAudio()
        {
            if (combatAudioSource == null || hitAudioClips == null || hitAudioClips.Length == 0) return;

            AudioClip clip = hitAudioClips[Random.Range(0, hitAudioClips.Length)];
            if (clip != null)
            {
                combatAudioSource.pitch = Random.Range(0.95f, 1.1f);
                combatAudioSource.PlayOneShot(clip);
            }
        }

        private void PlaySlashVFX()
        {
            if (_pooledSlashVfx == null || _pooledSlashVfx.Length == 0) return;

            _slashPoolIndex = (_slashPoolIndex + 1) % _pooledSlashVfx.Length;
            GameObject slash = _pooledSlashVfx[_slashPoolIndex];
            if (slash == null) return;

            Transform origin = weaponTransform != null ? weaponTransform : transform;
            slash.transform.position = origin.TransformPoint(hitboxCenterOffset);
            slash.transform.rotation = origin.rotation * Quaternion.Euler(hitboxRotationOffset);

            slash.SetActive(false);
            slash.SetActive(true);

            if (slash.TryGetComponent(out ParticleSystem ps))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }
        }

        #endregion

        #region Mirror Multiplayer Sync (Low-Bandwidth RPCs)

        [Command]
        private void CmdStartAttack(int comboIndex)
        {
            RpcOnAttack(comboIndex);
        }

        [ClientRpc(includeOwner = false)]
        private void RpcOnAttack(int comboIndex)
        {
            _currentComboIndex = comboIndex;
            if (_animator != null)
            {
                _animator.SetInteger(ComboIndexHash, comboIndex);
                _animator.SetBool(IsAttackingHash, true);
            }

            PlaySwingAudio();
            PlaySlashVFX();
        }

        [Command]
        private void CmdEndAttack()
        {
            RpcOnEndAttack();
        }

        [ClientRpc(includeOwner = false)]
        private void RpcOnEndAttack()
        {
            if (_animator != null)
            {
                _animator.SetBool(IsAttackingHash, false);
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
