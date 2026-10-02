using Mirror;
using PrimeTween;
using UnityEngine;
using Woodsmen.Inventory;

namespace Woodsmen.Environment
{
    /// <summary>
    /// Collectible gold coin dropped by defeated enemies in the world.
    /// Features:
    /// - Physical parabolic launch arc with random radial dispersion forces.
    /// - Smooth continuous Y-axis rotation and floating hover bobbing.
    /// - High-performance kinematic sleep after landing to eliminate physics overhead.
    /// - Snappy PrimeTween collection suck toward the player on trigger enter.
    /// - Object-pooled for zero garbage collection during intense combat waves.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CoinPickup : MonoBehaviour
    {
        [Header("Coin Value")]
        [Tooltip("Amount of currency/money granted when collected.")]
        [SerializeField] private int coinValue = 1;

        public int CoinValue
        {
            get => coinValue;
            set => coinValue = Mathf.Max(1, value);
        }

        public int CoinId { get; set; }
        public bool IsCollected => _isCollected;

        [Header("Collection Settings")]
        [Tooltip("Debounce delay in seconds before pickup can be collected. Allows coins to visually burst outward first.")]
        [SerializeField] private float pickupDelay = 0.35f;

        [Header("Visual Model (Rotating Transform)")]
        [Tooltip("Child visual transform that rotates independently of physical orientation.")]
        [SerializeField] private Transform visualModel;
        [SerializeField] private float rotationSpeed = 160f;
        [SerializeField] private float hoverBobAmplitude = 0.08f;
        [SerializeField] private float hoverBobFrequency = 3.0f;

        [Header("Physics & Ground Settling")]
        [SerializeField] private Rigidbody rb;
        [SerializeField] private Collider triggerCollider;
        [SerializeField] private Collider physicalCollider;
        [SerializeField] private float settleCheckDelay = 0.4f;

        [Header("Hibernation (Sleep LOD)")]
        [Tooltip("Time in seconds before the pickup stops spinning and disables Update() to save 100% CPU. Keeps trigger active so player can still collect.")]
        [SerializeField] private float hibernateDelay = 10f;

        [Header("Audio Feedback")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip[] pickupAudioClips;

        private CoinPickupPool _originPool;
        private Vector3 _originalVisualScale;
        private Vector3 _baseVisualLocalPos;
        private Tween _collectTween;
        private bool _isCollected;
        private bool _isSettled;
        private bool _hasContactedGround;
        private float _spawnTime;
        private Transform _targetCollector;

        private void Awake()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.constraints = RigidbodyConstraints.FreezeRotation;
                rb.linearDamping = 1.2f;
                rb.angularDamping = 2.0f;
            }

            if (visualModel == null)
            {
                visualModel = transform.Find("Visual") ?? (transform.childCount > 0 ? transform.GetChild(0) : transform);
            }

            if (visualModel != null && visualModel.localScale != Vector3.zero)
            {
                _originalVisualScale = visualModel.localScale;
            }
            else
            {
                _originalVisualScale = Vector3.one;
            }

            _baseVisualLocalPos = visualModel != null ? visualModel.localPosition : Vector3.zero;

            // Auto-cache colliders defensively
            if (triggerCollider == null || physicalCollider == null)
            {
                Collider[] cols = GetComponents<Collider>();
                foreach (var c in cols)
                {
                    if (c.isTrigger && triggerCollider == null) triggerCollider = c;
                    else if (!c.isTrigger && physicalCollider == null) physicalCollider = c;
                }
            }

            if (audioSource == null)
            {
                TryGetComponent(out audioSource);
            }
        }

        private void Update()
        {
            if (_isCollected) return;

            // 1. Continuous smooth rotation around world vertical axis
            if (visualModel != null)
            {
                visualModel.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

                // Subtle hovering bob once settled on the ground
                if (_isSettled)
                {
                    float bobOffset = Mathf.Sin((Time.time - _spawnTime) * hoverBobFrequency) * hoverBobAmplitude;
                    visualModel.localPosition = _baseVisualLocalPos + new Vector3(0f, bobOffset, 0f);
                }
            }

            // 2. High-performance physics sleep once settled (guaranteed freeze after 2.0s even on slopes)
            if (!_isSettled && rb != null)
            {
                bool timeReady = (Time.time - _spawnTime > 1.2f) || (_hasContactedGround && Time.time - _spawnTime > settleCheckDelay);
                if ((timeReady && rb.linearVelocity.sqrMagnitude < 0.25f) || (Time.time - _spawnTime > 2.0f))
                {
                    _isSettled = true;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true; // Zero PhysX calculations while resting
                    
                    // Immediately disable the physical collider so enemies don't get stuck on it
                    if (physicalCollider != null) physicalCollider.enabled = false;
                }
            }

            // 3. Loot Hibernation (Sleep LOD): hibernate after 3s to completely eliminate Update() overhead
            if (_isSettled && (Time.time - _spawnTime > 3.0f))
            {
                Hibernate();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Ignore collisions with other coin pickups
            if (collision.gameObject.TryGetComponent(out CoinPickup _)) return;
            _hasContactedGround = true;
        }

        private void OnCollisionExit(Collision collision)
        {
            // If the collider was manually disabled by settling, ignore this exit event
            if (physicalCollider != null && !physicalCollider.enabled) return;

            _hasContactedGround = false;
            if (_isSettled && !_isCollected && rb != null)
            {
                _isSettled = false;
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.WakeUp();
                this.enabled = true;
            }
        }

        /// <summary>
        /// Enters full hibernation mode: shuts down Update() and disables physical ground collider.
        /// The trigger collider remains active so players can still walk through to collect it at any time!
        /// </summary>
        private void Hibernate()
        {
            if (_isCollected || !_isSettled) return;

            _isSettled = true;
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            // Disable physical ground collider (PhysX zero-overhead)
            if (physicalCollider != null) physicalCollider.enabled = false;

            // Rest visual at natural base position
            if (visualModel != null)
            {
                visualModel.localPosition = _baseVisualLocalPos;
            }

            // Disable this MonoBehaviour to shut off Update() entirely (0 CPU script cost)
            this.enabled = false;
        }

        /// <summary>
        /// Launches the coin from a specified spawn location with an impulse velocity.
        /// Called by CoinPickupPool when spawning loot.
        /// </summary>
        public void Launch(Vector3 spawnPosition, Vector3 impulseForce, CoinPickupPool pool)
        {
            _originPool = pool;
            _isCollected = false;
            _isSettled = false;
            _hasContactedGround = false;
            _targetCollector = null;
            _spawnTime = Time.time;

            if (_collectTween.isAlive) _collectTween.Stop();

            transform.position = spawnPosition;
            transform.rotation = Quaternion.identity;

            if (visualModel != null)
            {
                visualModel.localScale = _originalVisualScale;
                visualModel.localPosition = _baseVisualLocalPos;
            }

            // Activate GameObject FIRST so PhysX registers it
            gameObject.SetActive(true);
            this.enabled = true;

            // Re-enable all colliders
            Collider[] colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = true;
            }

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = impulseForce;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_isCollected) return;

            // Debounce: allow coins to visually disperse into an arc first before becoming collectible
            if (Time.time - _spawnTime < pickupDelay) return;

            // Check if collider belongs to a player character
            if (!other.TryGetComponent(out PlayerInventory inventory))
            {
                inventory = other.GetComponentInParent<PlayerInventory>();
            }

            if (inventory != null)
            {
                // In multiplayer sessions, only the locally controlled player collects their local coins.
                // This prevents duplicate collection / double rewards between client and host.
                if (NetworkClient.active || NetworkServer.active)
                {
                    if (inventory.netIdentity != null && !inventory.netIdentity.isLocalPlayer)
                    {
                        return;
                    }
                }

                Collect(inventory, other.transform);
            }
        }

        private void Collect(PlayerInventory inventory, Transform collectorTransform)
        {
            if (_isCollected) return;
            _isCollected = true;
            _targetCollector = collectorTransform;

            // 1. Award money/coins to the player inventory (synchronized across network)
            inventory.CollectWorldCoin(CoinId, coinValue);

            // 2. Play collection audio if assigned
            PlayCollectionAudio();

            // 3. Disable colliders, physics, and animate into collector
            AnimateCollectAndRecycle(collectorTransform);
        }

        /// <summary>
        /// Called when another player in the multiplayer session collected this coin.
        /// Visually animates the coin flying toward that player and removes it from the world.
        /// </summary>
        public void RemoteCollect(Transform collectorTransform)
        {
            if (_isCollected) return;
            _isCollected = true;
            _targetCollector = collectorTransform;

            PlayCollectionAudio();
            AnimateCollectAndRecycle(collectorTransform);
        }

        private void PlayCollectionAudio()
        {
            if (audioSource != null && pickupAudioClips != null && pickupAudioClips.Length > 0)
            {
                AudioClip clip = pickupAudioClips[Random.Range(0, pickupAudioClips.Length)];
                if (clip != null)
                {
                    audioSource.pitch = Random.Range(1.05f, 1.25f);
                    audioSource.PlayOneShot(clip);
                }
            }
        }

        private void AnimateCollectAndRecycle(Transform collectorTransform)
        {
            // Disable all colliders and physics immediately so it cannot trigger again or block character
            Collider[] colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            // Shrink and disappear immediately into player, then recycle back to pool
            if (visualModel != null && visualModel.gameObject.activeInHierarchy)
            {
                _collectTween = Tween.Scale(visualModel, Vector3.zero, duration: 0.12f, ease: Ease.InBack)
                    .OnComplete(this, target => target.Recycle());
            }
            else
            {
                Recycle();
            }
        }

        private void Recycle()
        {
            if (_originPool != null)
            {
                _originPool.ReturnToPool(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
