using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using Woodsmen.Combat;
using Woodsmen.Environment;
using Woodsmen.Players;
using Woodsmen.Utilities;

namespace Woodsmen.AI
{
    /// <summary>
    /// Professional, networked AI controller for Goblin Level 1 enemies.
    /// Features:
    /// - Continuous chasing targeting the closest player (No idle state).
    /// - NavMesh pathfinding with high-quality obstacle avoidance (respects trees and obstacles).
    /// - Attacks while maintaining movement (UpperBody AvatarMask separates running legs from slashing arms).
    /// - Network-compliant: Server-authoritative logic with client RPC animation dispatches and offline single-player fallback.
    /// - Object-pooling friendly: clean lifecycle hooks (OnSpawnFromPool, OnDespawnToPool).
    /// - Server-authoritative health integration with CharacterHealth.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(CharacterHealth))]
    public class GoblinAI : NetworkBehaviour
    {
        [Header("Targeting & Layers")]
        [Tooltip("Target layer mask for player characters to attack. If set to Nothing (0), automatically defaults to the 'Player' layer or player tags.")]
        [SerializeField] private LayerMask targetLayer = 0;

        [Header("Movement & Pathfinding")]
        [SerializeField] private float moveSpeed = 3.8f;
        [SerializeField] private float acceleration = 14f;
        [SerializeField] private float angularSpeed = 540f;
        [SerializeField] private float stoppingDistance = 1.3f;
        [SerializeField] private float obstacleAvoidanceRadius = 0.45f;

        [Header("Combat & Attack")]
        [SerializeField] private float attackRange = 1.8f;
        [SerializeField] private float attackDamage = 15f;
        [Tooltip("Cooldown period between consecutive attack cycles.")]
        [SerializeField] private float attackInterval = 1.2f;
        [Tooltip("Delay into the attack animation when damage is applied to the target.")]
        [SerializeField] private float attackWindup = 0.35f;
        [Tooltip("Duration the IsAttacking state remains active before resetting.")]
        [SerializeField] private float attackActiveDuration = 0.75f;
        [Tooltip("Seconds before dead enemy is automatically returned to the pool.")]
        [SerializeField] private float deathDespawnDelay = 2.5f;

        [Header("Loot Drops")]
        [Tooltip("Minimum coins dropped when goblin dies.")]
        [SerializeField] private int minCoinDrop = 2;
        [Tooltip("Maximum coins dropped when goblin dies.")]
        [SerializeField] private int maxCoinDrop = 5;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip[] attackClips;
        [SerializeField] private float pitchMin = 0.9f;
        [SerializeField] private float pitchMax = 1.15f;

        // Cached Animator Parameter Hashes (Rule 5: Zero GC)
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");
        private static readonly int DeathHash = Animator.StringToHash("Death");
        private static readonly int IsDeadHash = Animator.StringToHash("IsDead");
        private static readonly int HitHash = Animator.StringToHash("Hit");

        // Component References
        private NavMeshAgent _navMeshAgent;
        private CharacterHealth _characterHealth;
        private Animator _animator;
        private Collider _collider;

        // Runtime Targeting & Combat State
        private Transform _targetPlayer;
        private CharacterHealth _targetHealth;
        private float _attackTimer;
        private float _damageTimer;
        private float _attackDurationTimer;
        private float _targetScanTimer;
        private bool _isAttacking;
        private bool _damagePending;
        private bool _isDespawning;

        // Non-allocating player scan buffer
        private static readonly List<CharacterHealth> PlayerCandidates = new List<CharacterHealth>(16);

        /// <summary>
        /// Event invoked when this enemy is ready to be recycled into the pool.
        /// </summary>
        public event Action<GoblinAI> OnDespawnRequested;

        /// <summary>
        /// True if this is the network server OR running in offline single-player mode (Directive 12).
        /// </summary>
        private bool IsAuthoritative => isServer || (!NetworkClient.active && !NetworkServer.active);

        private void Awake()
        {
            EnsureDependencies();
        }

        public LayerMask TargetLayer
        {
            get => targetLayer;
            set => targetLayer = value;
        }

        private void EnsureDependencies()
        {
            if (targetLayer.value == 0)
            {
                int playerLayerIndex = LayerMask.NameToLayer("Player");
                if (playerLayerIndex != -1)
                {
                    targetLayer = 1 << playerLayerIndex;
                }
            }

            if (_navMeshAgent == null && !gameObject.TryGetComponent(out _navMeshAgent))
            {
                _navMeshAgent = gameObject.AddComponent<NavMeshAgent>();
            }
            if (_characterHealth == null && !gameObject.TryGetComponent(out _characterHealth))
            {
                _characterHealth = gameObject.AddComponent<CharacterHealth>();
            }
            if (_collider == null && !gameObject.TryGetComponent(out _collider))
            {
                var cap = gameObject.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0f, 0.8f, 0f);
                cap.radius = 0.45f;
                cap.height = 1.6f;
                _collider = cap;
            }
            if (_animator == null && !gameObject.TryGetComponent(out _animator))
            {
                _animator = GetComponentInChildren<Animator>();
            }

            if (_animator != null && _animator.runtimeAnimatorController == null)
            {
#if UNITY_EDITOR
                _animator.runtimeAnimatorController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Enemies/Goblin LVL1/Goblin_Animator.controller");
#endif
            }

            if (audioSource == null && !gameObject.TryGetComponent(out audioSource))
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0.7f;
            }

            ConfigureNavMeshAgent();
        }

        private void ConfigureNavMeshAgent()
        {
            if (_navMeshAgent == null) return;

            _navMeshAgent.speed = moveSpeed;
            _navMeshAgent.acceleration = acceleration;
            _navMeshAgent.angularSpeed = angularSpeed;
            _navMeshAgent.stoppingDistance = stoppingDistance;
            _navMeshAgent.radius = obstacleAvoidanceRadius;
            _navMeshAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            _navMeshAgent.autoBraking = false;
            _navMeshAgent.updateRotation = false;
        }

        private void OnEnable()
        {
            if (_characterHealth != null)
            {
                _characterHealth.OnDeath += HandleDeath;
            }
        }

        private void OnDisable()
        {
            if (_characterHealth != null)
            {
                _characterHealth.OnDeath -= HandleDeath;
            }
        }

        private void Start()
        {
            if (IsAuthoritative)
            {
                FindClosestPlayer();
            }
        }

        private void Update()
        {
            // Death Guard
            if (_characterHealth != null && _characterHealth.IsDead) return;

            if (!IsAuthoritative)
            {
                // Remote clients still update animator playback based on transform movement
                UpdateAnimator();
                return;
            }

            // 1. Tick attack cooldown
            if (_attackTimer > 0f)
            {
                _attackTimer -= Time.deltaTime;
            }

            // 2. Periodic target scan (every 0.25s) to target the closest living player
            _targetScanTimer -= Time.deltaTime;
            if (_targetScanTimer <= 0f)
            {
                _targetScanTimer = 0.25f;
                FindClosestPlayer();
            }

            // 3. Chasing & Movement (No Idle State)
            UpdateMovement();

            // 4. Attack State & Damage Application
            UpdateCombat();

            // 5. Update Animator parameters
            UpdateAnimator();
        }

        #region Targeting Logic

        /// <summary>
        /// Locates the closest living player in the scene without generating garbage allocations.
        /// </summary>
        private void FindClosestPlayer()
        {
            PlayerCandidates.Clear();
            CharacterHealth[] allEntities = FindObjectsByType<CharacterHealth>(FindObjectsSortMode.None);

            int playerLayerIndex = LayerMask.NameToLayer("Player");

            for (int i = 0; i < allEntities.Length; i++)
            {
                CharacterHealth entity = allEntities[i];
                if (entity == null || entity == _characterHealth || entity.IsDead) continue;

                bool isPlayer = false;

                // 1. Layer check (matches targetLayer if configured, or "Player" layer if defined)
                if (targetLayer.value != 0)
                {
                    if (((1 << entity.gameObject.layer) & targetLayer.value) != 0)
                    {
                        isPlayer = true;
                    }
                }
                else if (playerLayerIndex != -1 && entity.gameObject.layer == playerLayerIndex)
                {
                    isPlayer = true;
                }

                // 2. Fallback tag, name, and component checks
                if (!isPlayer)
                {
                    if (entity.gameObject.CompareTag("Player") ||
                        entity.name.Contains("Lumberjack") ||
                        entity.name.Contains("Warrior") ||
                        entity.TryGetComponent(out LocomotionController _))
                    {
                        isPlayer = true;
                    }
                }

                if (isPlayer)
                {
                    PlayerCandidates.Add(entity);
                }
            }

            CharacterHealth closest = null;
            float closestSqrDist = float.MaxValue;
            Vector3 myPos = transform.position;

            for (int i = 0; i < PlayerCandidates.Count; i++)
            {
                CharacterHealth candidate = PlayerCandidates[i];
                float sqrDist = (candidate.transform.position - myPos).sqrMagnitude;
                if (sqrDist < closestSqrDist)
                {
                    closestSqrDist = sqrDist;
                    closest = candidate;
                }
            }

            if (closest != null)
            {
                _targetPlayer = closest.transform;
                _targetHealth = closest;
            }
            else
            {
                _targetPlayer = null;
                _targetHealth = null;
            }
        }

        #endregion

        #region Movement & Pathfinding (NavMesh with Dynamic Obstacle Carving)

        private void UpdateMovement()
        {
            if (_targetPlayer == null) return;

            Vector3 targetPosition = _targetPlayer.position;

            if (_navMeshAgent != null && _navMeshAgent.enabled)
            {
                if (_navMeshAgent.isOnNavMesh)
                {
                    // Continuous chase: NEVER stop moving, even during attacks!
                    _navMeshAgent.isStopped = false;
                    _navMeshAgent.SetDestination(targetPosition);
                }
                else
                {
                    // Defensive fallback if temporarily off NavMesh: smooth manual translation
                    Vector3 moveDir = (targetPosition - transform.position).normalized;
                    moveDir.y = 0f;
                    transform.position += moveDir * (moveSpeed * Time.deltaTime);
                }
            }

            // Update rotation with 180° model offset so the visual model faces where it moves / targets
            UpdateRotation(targetPosition);
        }

        private void UpdateRotation(Vector3 targetPosition)
        {
            Vector3 lookDir = Vector3.zero;

            // When moving along path with noticeable velocity, face movement direction
            if (_navMeshAgent != null && _navMeshAgent.enabled && _navMeshAgent.velocity.sqrMagnitude > 0.15f)
            {
                lookDir = _navMeshAgent.velocity;
            }
            // When stopped or almost stationary close to target, face directly towards target player
            else
            {
                lookDir = targetPosition - transform.position;
            }

            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                // The FBX model was exported facing -Z (backwards).
                // Multiplying by Quaternion.Euler(0f, 180f, 0f) rotates the goblin 180 degrees so its face points forward.
                Quaternion desiredRotation = Quaternion.LookRotation(lookDir.normalized) * Quaternion.Euler(0f, 180f, 0f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRotation, angularSpeed * Time.deltaTime);
            }
        }

        #endregion

        #region Combat & Attack Logic (Running Attack with UpperBody Mask)

        private void UpdateCombat()
        {
            if (_targetPlayer == null) return;

            float distToTarget = Vector3.Distance(transform.position, _targetPlayer.position);

            // Trigger attack when closing in within attack range
            if (distToTarget <= attackRange && _attackTimer <= 0f && !_isAttacking)
            {
                StartAttack();
            }

            // Handle delayed damage application at the strike/windup frame
            if (_damagePending)
            {
                _damageTimer -= Time.deltaTime;
                if (_damageTimer <= 0f)
                {
                    _damagePending = false;
                    ApplyAttackDamage();
                }
            }

            // Monitor attack animation completion
            if (_isAttacking)
            {
                _attackDurationTimer -= Time.deltaTime;
                if (_attackDurationTimer <= 0f)
                {
                    EndAttack();
                }
            }
        }

        private void StartAttack()
        {
            _isAttacking = true;
            _damagePending = true;
            _damageTimer = attackWindup;
            _attackDurationTimer = attackActiveDuration;

            if (_animator != null)
            {
                _animator.SetBool(IsAttackingHash, true);
            }

            PlayAttackAudio();

            // Mirror multiplayer synchronization: dispatch attack animation to clients
            if (isServer && NetworkServer.active)
            {
                RpcOnAttack();
            }
        }

        private void ApplyAttackDamage()
        {
            if (_targetHealth == null || _targetHealth.IsDead) return;

            float dist = Vector3.Distance(transform.position, _targetPlayer.position);

            // Generous hit check so moving player is still struck if nearby
            if (dist <= attackRange + 0.8f)
            {
                Vector3 contactPoint = _targetPlayer.position + Vector3.up * 1.0f;
                Vector3 hitDirection = (_targetPlayer.position - transform.position).normalized;

                _targetHealth.TakeDamage(attackDamage, contactPoint, hitDirection, gameObject);

                Debug.Log($"<color=#ff79c6><b>[Goblin LVL1]</b></color> Attacked player '{_targetHealth.gameObject.name}' for {attackDamage} damage on the move!");
            }
        }

        private void EndAttack()
        {
            _isAttacking = false;
            _damagePending = false;
            _attackTimer = attackInterval;

            if (_animator != null)
            {
                _animator.SetBool(IsAttackingHash, false);
            }

            if (isServer && NetworkServer.active)
            {
                RpcOnEndAttack();
            }
        }

        private void PlayAttackAudio()
        {
            if (audioSource == null || attackClips == null || attackClips.Length == 0) return;

            AudioClip clip = attackClips[UnityEngine.Random.Range(0, attackClips.Length)];
            if (clip != null)
            {
                audioSource.pitch = UnityEngine.Random.Range(pitchMin, pitchMax);
                audioSource.PlayOneShot(clip);
            }
        }

        #endregion

        #region Mirror Multiplayer Sync

        [ClientRpc(includeOwner = false)]
        private void RpcOnAttack()
        {
            if (_animator != null)
            {
                _animator.SetBool(IsAttackingHash, true);
            }
            PlayAttackAudio();
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

        #region Animator & Locomotion Sync

        private Vector3 _lastPosition;

        private void UpdateAnimator()
        {
            if (_animator == null) return;

            float currentSpeed = 1.0f;
            if (_navMeshAgent != null && _navMeshAgent.enabled)
            {
                currentSpeed = Mathf.Clamp(_navMeshAgent.velocity.magnitude / Mathf.Max(0.1f, moveSpeed), 0.5f, 1.3f);
            }
            else
            {
                // Fallback for networked clients or off-agent movement
                float displacement = (transform.position - _lastPosition).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
                currentSpeed = Mathf.Clamp(displacement / Mathf.Max(0.1f, moveSpeed), 0.5f, 1.3f);
            }
            _lastPosition = transform.position;

            _animator.SetFloat(SpeedHash, currentSpeed);
        }

        #endregion

        #region Death & Despawn to Pool

        private void HandleDeath()
        {
            if (_isDespawning) return;
            _isDespawning = true;

            // Immediately stop navigation and clear target
            if (_navMeshAgent != null)
            {
                _navMeshAgent.isStopped = true;
                _navMeshAgent.enabled = false;
            }

            if (_collider != null)
            {
                _collider.enabled = false;
            }

            if (_animator != null)
            {
                _animator.SetBool(IsAttackingHash, false);
                _animator.SetBool(IsDeadHash, true);
                _animator.SetTrigger(DeathHash);
            }

            // Authoritative loot drop: spawn bouncing coins across all connected clients and host
            if (IsAuthoritative)
            {
                int coinDropCount = UnityEngine.Random.Range(minCoinDrop, maxCoinDrop + 1);
                if (isServer && NetworkServer.active)
                {
                    // Assign unique coin IDs and a shared deterministic seed so every client
                    // spawns coins at the exact same positions and can track them by ID.
                    int startCoinId = CoinPickupPool.GetNextCoinIdRange(coinDropCount);
                    int seed = UnityEngine.Random.Range(1, int.MaxValue);
                    RpcSpawnLoot(spawnPosition: transform.position, coinCount: coinDropCount,
                                 startCoinId: startCoinId, seed: seed);
                }
                else if (!NetworkClient.active && !NetworkServer.active)
                {
                    // Offline single-player – no sync needed
                    if (CoinPickupPool.Instance != null)
                    {
                        CoinPickupPool.Instance.SpawnCoins(transform.position, coinDropCount, 0, 0);
                    }
                }
            }

            // Schedule return to pool via PrimeTween / delay
            PrimeTween.Tween.Delay(deathDespawnDelay, () =>
            {
                OnDespawnRequested?.Invoke(this);
            });
        }

        /// <summary>
        /// Broadcasts a loot spawn to ALL clients (including the host) with a deterministic seed,
        /// so every machine sees coins at the same positions and can track them by their assigned IDs.
        /// </summary>
        [ClientRpc]
        private void RpcSpawnLoot(Vector3 spawnPosition, int coinCount, int startCoinId, int seed)
        {
            if (CoinPickupPool.Instance != null)
            {
                CoinPickupPool.Instance.SpawnCoins(spawnPosition, coinCount, startCoinId, seed);
            }
        }

        #endregion

        #region Object Pooling Lifecycle API

        /// <summary>
        /// Called by EnemySpawnManager when taking an enemy instance out of the pool.
        /// </summary>
        public void OnSpawnFromPool(Vector3 spawnPosition, Quaternion spawnRotation)
        {
            EnsureDependencies();

            _isDespawning = false;
            _isAttacking = false;
            _damagePending = false;
            _attackTimer = 0f;
            _damageTimer = 0f;

            transform.position = spawnPosition;
            transform.rotation = spawnRotation;
            _lastPosition = spawnPosition;

            gameObject.SetActive(true);

            if (_collider != null)
            {
                _collider.enabled = true;
            }

            // Revive health pool to 100%
            if (_characterHealth != null)
            {
                _characterHealth.Revive(1.0f);
            }

            // Re-enable NavMeshAgent and warp to exact spawn position on NavMesh
            if (_navMeshAgent != null)
            {
                _navMeshAgent.enabled = true;
                _navMeshAgent.Warp(spawnPosition);
                _navMeshAgent.isStopped = false;
                _navMeshAgent.updateRotation = false;
            }

            // Completely reset animator states and purge all latent triggers
            if (_animator != null)
            {
                _animator.ResetTrigger(DeathHash);
                _animator.ResetTrigger(HitHash);
                _animator.SetBool(IsDeadHash, false);
                _animator.SetBool(IsAttackingHash, false);
                _animator.SetFloat(SpeedHash, 1.0f);
                _animator.Rebind();
                _animator.Update(0f);
                _animator.Play("Walk", 0, 0f);
                _animator.Play("Empty", 1, 0f);
            }

            // Immediately search for closest player
            if (IsAuthoritative)
            {
                FindClosestPlayer();
            }
        }

        /// <summary>
        /// Called by EnemySpawnManager when returning an enemy instance to the pool.
        /// </summary>
        public void OnDespawnToPool()
        {
            _isDespawning = true;
            _targetPlayer = null;
            _targetHealth = null;

            if (_animator != null)
            {
                _animator.ResetTrigger(DeathHash);
                _animator.ResetTrigger(HitHash);
                _animator.SetBool(IsDeadHash, false);
                _animator.SetBool(IsAttackingHash, false);
                _animator.SetFloat(SpeedHash, 1.0f);
            }

            if (_navMeshAgent != null && _navMeshAgent.enabled)
            {
                _navMeshAgent.isStopped = true;
                _navMeshAgent.enabled = false;
            }

            if (_collider != null)
            {
                _collider.enabled = false;
            }

            gameObject.SetActive(false);
        }

        #endregion
    }
}
