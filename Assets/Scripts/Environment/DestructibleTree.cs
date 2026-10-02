using PrimeTween;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using Woodsmen.CameraSystem;
using Woodsmen.Combat;

namespace Woodsmen.Environment
{
    /// <summary>
    /// Represents a dynamic, destructible tree with standard numerical health points (HP).
    /// Can be damaged by the Lumberjack's axe, weapons, or environmental hazards.
    /// Implements:
    /// - Standard numerical health pool (maxHealth, currentHealth).
    /// - IDamageable interface for unified combat routing.
    /// - NavMeshObstacle with Carve = true (no runtime rebaking, Rule 2).
    /// - PrimeTween squash &amp; stretch wobble on impact (no Coroutines, Rule 1 &amp; Rule 4).
    /// - Audio and wood chip particle feedback.
    /// - Transitions into a tree stump upon being felled and drops wood materials.
    /// - Multiplayer sync is handled externally by TreeSyncManager (single NetworkIdentity for
    ///   ALL trees – zero per-tree Mirror overhead regardless of scene tree count).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DestructibleTree : MonoBehaviour, IDamageable
    {
        [Header("Tree Health")]
        [Tooltip("Maximum numerical health of this tree.")]
        [FormerlySerializedAs("maxHits")]
        [SerializeField] private float maxHealth = 100f;

        [Tooltip("Current remaining health of this tree.")]
        [FormerlySerializedAs("currentHits")]
        [SerializeField] private float currentHealth;

        [Tooltip("Minimum time interval in seconds between registered hits to prevent accidental duplicate hits.")]
        [SerializeField] private float hitDebounceTime = 0.15f;

        [Header("Felled Replacement (Stump)")]
        [Tooltip("Optional stump prefab to spawn when tree is chopped down (e.g. Stump_01).")]
        [SerializeField] private GameObject stumpPrefab;

        [Header("Resource Drops")]
        [Tooltip("Minimum wood dropped when the tree is felled.")]
        [SerializeField] private int minWoodDrop = 3;

        [Tooltip("Maximum wood dropped when the tree is felled (inclusive).")]
        [SerializeField] private int maxWoodDrop = 7;

        [Header("Impact Feedback Settings")]
        [SerializeField] private Vector3 punchScale = new Vector3(0.12f, -0.08f, 0.12f);
        [SerializeField] private float punchDuration = 0.25f;
        [SerializeField] private ParticleSystem woodChipsParticle;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip[] chopAudioClips;
        [SerializeField] private AudioClip treeFallAudioClip;

        private NavMeshObstacle _navObstacle;
        private Collider _treeCollider;
        private Tween _wobbleTween;
        private Vector3 _originalScale;
        private float _lastHitTime = -100f;
        private bool _isFelled;

        // Stable scene-unique ID assigned by TreeSyncManager on registration.
        // Negative value means this tree has not been registered yet.
        internal int TreeId { get; private set; } = -1;

        public bool IsFelled => _isFelled;
        public bool IsDead => _isFelled;
        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public float HealthNormalized => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
        public int MinWoodDrop => minWoodDrop;
        public int MaxWoodDrop => maxWoodDrop;

        [System.Obsolete("Use CurrentHealth instead.")]
        public int CurrentHits => Mathf.CeilToInt(currentHealth);

        [System.Obsolete("Use MaxHealth instead.")]
        public int MaxHits => Mathf.CeilToInt(maxHealth);

        /// <summary>
        /// Invoked whenever the tree takes damage. Arguments: (currentHealth, maxHealth).
        /// </summary>
        public event System.Action<float, float> OnHealthChanged;

        private void Awake()
        {
            _originalScale = transform.localScale;

            if (maxHealth <= 0f) maxHealth = 100f;
            if (currentHealth <= 0f) currentHealth = maxHealth;

            gameObject.TryGetComponent(out _treeCollider);

            if (!gameObject.TryGetComponent(out _navObstacle))
                _navObstacle = gameObject.AddComponent<NavMeshObstacle>();

            _navObstacle.carving = true;
            _navObstacle.carveOnlyStationary = true;

            if (audioSource == null) gameObject.TryGetComponent(out audioSource);
        }

        private void Start()
        {
            // Derive a stable ID from world position BEFORE registering.
            // Every client loads the same scene, so the same tree is always at the
            // same world-space position → identical ID on host and all clients.
            // This removes any dependency on registration order.
            TreeId = ComputePositionId(transform.position);
            TreeSyncManager.Register(this);
        }

        private void OnDestroy()
        {
            if (_wobbleTween.isAlive) _wobbleTween.Stop();
            TreeSyncManager.Unregister(this);
        }

        // ─── Stable position-based ID ─────────────────────────────────────────────

        /// <summary>
        /// Converts a world-space position into a stable integer key.
        /// Quantized to 0.1-unit precision to absorb minor float differences between builds.
        /// </summary>
        private static int ComputePositionId(Vector3 pos)
        {
            int x = Mathf.RoundToInt(pos.x * 10f);
            int y = Mathf.RoundToInt(pos.y * 10f);
            int z = Mathf.RoundToInt(pos.z * 10f);
            unchecked
            {
                int h = 17;
                h = h * 31 + x;
                h = h * 31 + y;
                h = h * 31 + z;
                return h;
            }
        }

        // ─── IDamageable ─────────────────────────────────────────────────────────

        /// <summary>
        /// Entry point for any damage source (axe, weapons, etc.).
        /// Forwards to Chop which handles both offline and online routing.
        /// </summary>
        public void TakeDamage(float damage, Vector3 hitPoint = default, Vector3 hitDirection = default, GameObject attacker = null)
        {
            Chop(damage, hitPoint);
        }

        /// <summary>
        /// Called when the Lumberjack's axe contacts this tree.
        /// In a networked session the damage request is routed through TreeSyncManager
        /// (a single NetworkBehaviour) so NO per-tree NetworkIdentity is needed.
        /// </summary>
        public void Chop(float damage = 25f, Vector3 hitPoint = default)
        {
            if (_isFelled) return;

            // Route through the sync manager when networking is active.
            // The manager owns the single NetworkIdentity for all trees.
            if (TreeSyncManager.IsNetworked && TreeId >= 0 && TreeSyncManager.Instance != null)
            {
                TreeSyncManager.Instance.RequestChop(TreeId, damage, hitPoint);
            }
            else
            {
                // Offline / single-player, or TreeSyncManager not yet in scene: apply directly.
                ApplyChopLocally(damage, hitPoint);
            }
        }

        // ─── Internal methods called by TreeSyncManager (all clients) ────────────

        /// <summary>
        /// Applies damage and runs hit feedback. Called by TreeSyncManager on every client.
        /// </summary>
        internal void ApplyChopLocally(float damage, Vector3 hitPoint)
        {
            if (_isFelled) return;
            if (Time.time - _lastHitTime < hitDebounceTime) return;
            _lastHitTime = Time.time;

            currentHealth = Mathf.Max(0f, currentHealth - damage);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            PlayHitFeedback(hitPoint);

            if (currentHealth <= 0f)
            {
                FellTree();
            }
        }

        /// <summary>
        /// Plays hit VFX/audio/wobble without changing health.
        /// Called separately by TreeSyncManager on non-authoritative clients.
        /// </summary>
        internal void PlayHitFeedback(Vector3 hitPoint)
        {
            // 1. PrimeTween Squash and Stretch Punch
            if (_wobbleTween.isAlive)
            {
                _wobbleTween.Stop();
                transform.localScale = _originalScale;
            }
            _wobbleTween = Tween.PunchScale(transform, punchScale, duration: punchDuration, frequency: 10);

            // 2. VFX particles
            Vector3 impactNormal = hitPoint != default ? (transform.position - hitPoint).normalized : Vector3.up;
            if (Woodsmen.Feedback.VFXManager.Instance != null)
            {
                Woodsmen.Feedback.VFXManager.Instance.PlayWoodImpact(
                    hitPoint != default ? hitPoint : transform.position, impactNormal);
            }
            else if (woodChipsParticle != null)
            {
                if (hitPoint != default) woodChipsParticle.transform.position = hitPoint;
                woodChipsParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                woodChipsParticle.Play(true);
            }

            // 3. Audio
            if (audioSource != null && chopAudioClips != null && chopAudioClips.Length > 0)
            {
                AudioClip clip = chopAudioClips[Random.Range(0, chopAudioClips.Length)];
                if (clip != null)
                {
                    audioSource.pitch = Random.Range(0.85f, 1.15f);
                    audioSource.PlayOneShot(clip);
                }
            }

            // 4. Camera Shake (local screen only)
            CameraManager.Instance?.ShakeTreeHit();
        }

        /// <summary>
        /// Performs the local felling sequence: disables collider, plays audio,
        /// spawns stump, and destroys the tree object. Called on every client by TreeSyncManager.
        /// </summary>
        internal void FellTree()
        {
            if (_isFelled) return;
            _isFelled = true;

            if (_treeCollider != null) _treeCollider.enabled = false;

            CameraManager.Instance?.ShakeTreeFell(transform.position);

            if (audioSource != null && treeFallAudioClip != null)
            {
                audioSource.pitch = 1.0f;
                audioSource.PlayOneShot(treeFallAudioClip);
            }

            if (stumpPrefab != null)
            {
                GameObject stump = Instantiate(stumpPrefab, transform.position, transform.rotation);
                if (!stump.TryGetComponent(out NavMeshObstacle stumpObstacle))
                    stumpObstacle = stump.AddComponent<NavMeshObstacle>();
                stumpObstacle.carving = true;
                stumpObstacle.carveOnlyStationary = true;
            }

            Destroy(gameObject, 0.05f);
        }
    }
}
