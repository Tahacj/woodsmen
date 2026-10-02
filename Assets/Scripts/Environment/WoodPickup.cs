using PrimeTween;
using UnityEngine;
using Woodsmen.Inventory;

namespace Woodsmen.Environment
{
    /// <summary>
    /// Collectible wood log item dropped in the world when a tree is felled.
    /// Features:
    /// - Physical parabolic launch arc with random radial dispersion forces.
    /// - Smooth continuous Y-axis rotation and floating hover bobbing.
    /// - High-performance kinematic sleep after landing to eliminate physics overhead.
    /// - Snappy PrimeTween collection suck toward the player on trigger enter.
    /// - Object-pooled for zero garbage collection during intense tree chopping.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class WoodPickup : MonoBehaviour
    {
        [Header("Wood Value")]
        [Tooltip("Amount of wood granted when collected (1 prefab = 1 wood).")]
        [SerializeField] private int woodValue = 1;

        public int WoodValue
        {
            get => woodValue;
            set => woodValue = Mathf.Max(1, value);
        }

        public int WoodId { get; set; }

        public bool IsCollected => _isCollected;

        [Header("Collection Settings")]
        [Tooltip("Debounce delay in seconds before pickup can be collected. Allows items to visually burst outward first.")]
        [SerializeField] private float pickupDelay = 0.35f;

        [Header("Visual Model (Rotating Transform)")]
        [Tooltip("Child visual transform that rotates independently of physical orientation.")]
        [SerializeField] private Transform visualModel;
        [SerializeField] private float rotationSpeed = 120f;
        [SerializeField] private float hoverBobAmplitude = 0.08f;
        [SerializeField] private float hoverBobFrequency = 2.5f;

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

        private WoodPickupPool _originPool;
        private Vector3 _originalVisualScale;
        private Vector3 _baseVisualLocalPos;
        private Tween _collectTween;
        private bool _isCollected;
        private bool _isSettled;
        private bool _hasContactedGround;
        private float _spawnTime;
        private Transform _targetCollector;
        private Vector3 _startCollectPos;

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
                _originalVisualScale = Vector3.one * 1.3f;
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

            // 2. High-performance physics sleep once settled (guaranteed freeze after 2.0s even on slopes/stumps)
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
            // Ignore collisions with other wood pickups
            if (collision.gameObject.TryGetComponent(out WoodPickup _)) return;
            _hasContactedGround = true;
        }

        private void OnCollisionExit(Collision collision)
        {
            // If the collider was manually disabled by settling, ignore this exit event
            if (physicalCollider != null && !physicalCollider.enabled) return;

            // If the pickup was pushed or fell off a ledge, resume physics
            _hasContactedGround = false;
            if (_isSettled && !_isCollected)
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
            if (!rb.isKinematic)
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
        /// Launches this pickup with an upward pop and radial dispersion impulse.
        /// </summary>
        public void Launch(Vector3 spawnPosition, Vector3 impulseForce, WoodPickupPool pool)
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

            // Debounce: allow items to visually disperse into an arc first before becoming collectible
            if (Time.time - _spawnTime < pickupDelay) return;

            // Check if collider belongs to a player character
            if (!other.TryGetComponent(out PlayerInventory inventory))
            {
                inventory = other.GetComponentInParent<PlayerInventory>();
            }

            if (inventory != null)
            {
                // In multiplayer sessions, only the locally controlled player collects their local items.
                if (Mirror.NetworkClient.active || Mirror.NetworkServer.active)
                {
                    if (inventory.netIdentity != null && !inventory.netIdentity.isLocalPlayer)
                    {
                        return; // Remote players cannot trigger local pickups
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

            // 1. Award wood to the player inventory and broadcast network despawn
            inventory.CollectWorldWood(WoodId, woodValue);

            // 2. Play collection audio if assigned
            if (audioSource != null && pickupAudioClips != null && pickupAudioClips.Length > 0)
            {
                AudioClip clip = pickupAudioClips[Random.Range(0, pickupAudioClips.Length)];
                if (clip != null)
                {
                    audioSource.pitch = Random.Range(1.05f, 1.25f);
                    audioSource.PlayOneShot(clip);
                }
            }

            // 3. Disable all colliders and physics immediately so it cannot trigger again or block character
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

            // 4. Shrink and disappear immediately into player, then recycle back to pool
            // Crucial: keep this.enabled active so PrimeTween completes its callback reliably
            if (visualModel != null && visualModel.gameObject.activeInHierarchy)
            {
                _collectTween = Tween.Scale(visualModel, Vector3.zero, duration: 0.10f, ease: Ease.InBack)
                    .OnComplete(this, target => target.Recycle());
            }
            else
            {
                Recycle();
            }
        }

        /// <summary>
        /// Initiates a forced visual collection animation driven by the network server.
        /// Does NOT award wood (the server already did that authoritatively).
        /// </summary>
        public void RemoteCollect(Transform remoteCollector)
        {
            if (_isCollected) return;
            _isCollected = true;
            _targetCollector = remoteCollector;

            if (audioSource != null && pickupAudioClips != null && pickupAudioClips.Length > 0)
            {
                AudioClip clip = pickupAudioClips[Random.Range(0, pickupAudioClips.Length)];
                if (clip != null)
                {
                    audioSource.pitch = Random.Range(1.05f, 1.25f);
                    audioSource.PlayOneShot(clip);
                }
            }

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

            if (visualModel != null && visualModel.gameObject.activeInHierarchy)
            {
                _collectTween = Tween.Scale(visualModel, Vector3.zero, duration: 0.15f, ease: Ease.InBack)
                    .OnComplete(this, target => target.Recycle());
            }
            else
            {
                Recycle();
            }
        }

        private void Recycle()
        {
            if (_collectTween.isAlive)
            {
                _collectTween.Stop();
            }

            gameObject.SetActive(false);
            this.enabled = false;
            woodValue = 1;

            if (_originPool != null)
            {
                _originPool.ReturnToPool(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_collectTween.isAlive)
            {
                _collectTween.Stop();
            }
        }
    }
}
