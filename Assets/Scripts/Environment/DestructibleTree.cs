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
    /// - PrimeTween squash & stretch wobble on impact (no Coroutines, Rule 1 & Rule 4).
    /// - Audio and wood chip particle feedback.
    /// - Transitions into a tree stump upon being felled and drops wood materials.
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
        [SerializeField] private int woodResourceAmount = 5;

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

        public bool IsFelled => _isFelled;
        public bool IsDead => _isFelled;
        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public float HealthNormalized => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

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

            if (maxHealth <= 0f)
            {
                maxHealth = 100f;
            }

            // Initialize current health if not pre-configured
            if (currentHealth <= 0f)
            {
                currentHealth = maxHealth;
            }

            gameObject.TryGetComponent(out _treeCollider);

            // Ensure NavMeshObstacle with Carve = true for dynamic navigation
            if (!gameObject.TryGetComponent(out _navObstacle))
            {
                _navObstacle = gameObject.AddComponent<NavMeshObstacle>();
            }

            _navObstacle.carving = true;
            _navObstacle.carveOnlyStationary = false;

            if (audioSource == null)
            {
                gameObject.TryGetComponent(out audioSource);
            }
        }

        private void OnDestroy()
        {
            if (_wobbleTween.isAlive)
            {
                _wobbleTween.Stop();
            }
        }

        /// <summary>
        /// General damage intake method for weapons, combat, or environmental hazards (IDamageable).
        /// </summary>
        /// <param name="damage">Damage amount.</param>
        /// <param name="hitPoint">Impact position.</param>
        /// <param name="hitDirection">Direction of the impact.</param>
        /// <param name="attacker">GameObject initiating the attack.</param>
        public void TakeDamage(float damage, Vector3 hitPoint = default, Vector3 hitDirection = default, GameObject attacker = null)
        {
            Chop(damage, hitPoint);
        }

        /// <summary>
        /// Called when the Lumberjack's axe hitbox contacts this tree.
        /// </summary>
        /// <param name="damage">Health points to subtract.</param>
        /// <param name="hitPoint">Position of the impact.</param>
        public void Chop(float damage = 25f, Vector3 hitPoint = default)
        {
            if (_isFelled) return;

            // Debounce guard: reject any duplicate hit events within the debounce window
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

        private void PlayHitFeedback(Vector3 hitPoint)
        {
            // 1. PrimeTween Squash and Stretch Punch (Rule 4: Zero GC, procedural animation)
            if (_wobbleTween.isAlive)
            {
                _wobbleTween.Stop();
                transform.localScale = _originalScale;
            }

            _wobbleTween = Tween.PunchScale(transform, punchScale, duration: punchDuration, frequency: 10);

            // 2. Wood chip and dust particle emission (Zero-GC Pool with fallback)
            Vector3 impactNormal = hitPoint != default ? (transform.position - hitPoint).normalized : Vector3.up;
            if (Woodsmen.Feedback.VFXManager.Instance != null)
            {
                Woodsmen.Feedback.VFXManager.Instance.PlayWoodImpact(hitPoint != default ? hitPoint : transform.position, impactNormal);
            }
            else if (woodChipsParticle != null)
            {
                if (hitPoint != default)
                {
                    woodChipsParticle.transform.position = hitPoint;
                }
                woodChipsParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                woodChipsParticle.Play(true); // Triggers both parent and child ParticleSystems
            }

            // 3. Audio feedback with pitch variation
            if (audioSource != null && chopAudioClips != null && chopAudioClips.Length > 0)
            {
                AudioClip clip = chopAudioClips[Random.Range(0, chopAudioClips.Length)];
                if (clip != null)
                {
                    audioSource.pitch = Random.Range(0.85f, 1.15f);
                    audioSource.PlayOneShot(clip);
                }
            }

            // 4. Camera Shake hook (Only triggers for the local player's screen)
            CameraManager.Instance?.ShakeTreeHit();
        }

        private void FellTree()
        {
            _isFelled = true;

            // Distance-attenuated camera shake when the tree collapses
            CameraManager.Instance?.ShakeTreeFell(transform.position);

            // Play fall sound
            if (audioSource != null && treeFallAudioClip != null)
            {
                audioSource.pitch = 1.0f;
                audioSource.PlayOneShot(treeFallAudioClip);
            }

            // Spawn Stump in place if configured
            if (stumpPrefab != null)
            {
                GameObject stump = Instantiate(stumpPrefab, transform.position, transform.rotation);
                // Ensure stump carves the NavMesh
                if (!stump.TryGetComponent(out NavMeshObstacle stumpObstacle))
                {
                    stumpObstacle = stump.AddComponent<NavMeshObstacle>();
                }
                stumpObstacle.carving = true;
            }

            Debug.Log($"[Woodsmen] Tree felled at {transform.position}! Dropped {woodResourceAmount} wood.");

            // Destroy the main tree object after a brief moment or immediately
            Destroy(gameObject, 0.05f);
        }
    }
}
