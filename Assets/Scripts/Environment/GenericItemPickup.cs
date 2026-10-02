using Mirror;
using PrimeTween;
using UnityEngine;
using Woodsmen.Inventory;

namespace Woodsmen.Environment
{
    /// <summary>
    /// Networked pickup for world-dropped items (consumables, potions, etc.).
    ///
    /// Collection flow (networked):
    ///   1. Local player's trigger fires → Collect() → inventory.CollectGenericPickup(pickupId, itemId, qty)
    ///   2. Server atomically claims via GenericPickupPool.ConsumePickup()
    ///   3. Server awards item, then RpcDespawnGenericPickup(pickupId) tells all clients
    ///   4. Each client calls DespawnPickup() → RemoteCollect() → shrink + destroy
    ///
    /// Collection flow (offline):
    ///   Collect() → inventory.AddItem() directly, PlayCollectEffectsAndDestroy().
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GenericItemPickup : MonoBehaviour
    {
        [Header("Item Data")]
        [SerializeField] private string itemId;
        [SerializeField] private int quantity = 1;

        [Header("Collection Settings")]
        [SerializeField] private float pickupDelay = 0.5f;

        [Header("Visual Model")]
        [SerializeField] private Transform visualModel;
        [SerializeField] private float rotationSpeed = 90f;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip pickupSound;

        // Network identity for authoritative claiming (0 = offline / untracked)
        public int PickupId { get; private set; }

        private Rigidbody _rb;
        private Collider _triggerCollider;
        private bool _isCollected;
        private float _spawnTime;

        // ─── Initialization ───────────────────────────────────────────────────────

        /// <summary>Called by SpawnWorldDrop to configure item data and network ID.</summary>
        public void Initialize(string id, int amount, int pickupId = 0)
        {
            itemId    = id;
            quantity  = amount;
            PickupId  = pickupId;

            if (pickupId > 0)
                GenericPickupPool.Instance.Register(pickupId, this);
        }

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // Auto-locate visual child
            if (visualModel == null)
            {
                Transform visual = transform.Find("Visual");
                if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
                visualModel = visual != null ? visual : transform;
            }

            // Find trigger collider
            foreach (var col in GetComponents<Collider>())
                if (col.isTrigger) { _triggerCollider = col; break; }

            if (audioSource == null) TryGetComponent(out audioSource);
        }

        private void Start()
        {
            _spawnTime = Time.time;

            // Pop-out arc
            if (_rb != null)
            {
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.5f;
                _rb.linearVelocity = dir.normalized * Random.Range(3f, 5f);
            }
        }

        private void Update()
        {
            if (_isCollected) return;
            if (visualModel != null)
                visualModel.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
        }

        private void OnDestroy()
        {
            // Clean up registry entry if the object is destroyed by other means
            if (PickupId > 0) GenericPickupPool.Instance.Unregister(PickupId);
        }

        // ─── Trigger ──────────────────────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (_isCollected) return;
            if (Time.time - _spawnTime < pickupDelay) return;
            if (string.IsNullOrEmpty(itemId)) return;

            // Only the local player should initiate collection
            if (NetworkClient.active || NetworkServer.active)
            {
                if (!other.TryGetComponent(out NetworkIdentity netId))
                    netId = other.GetComponentInParent<NetworkIdentity>();
                if (netId == null || !netId.isLocalPlayer) return;
            }

            PlayerInventory inventory = other.GetComponent<PlayerInventory>()
                                     ?? other.GetComponentInParent<PlayerInventory>();
            if (inventory != null)
                Collect(inventory);
        }

        // ─── Collection ───────────────────────────────────────────────────────────

        private void Collect(PlayerInventory inventory)
        {
            if (_isCollected) return;
            _isCollected = true;

            // Disable collider immediately so it can't be triggered again locally
            if (_triggerCollider != null) _triggerCollider.enabled = false;

            inventory.CollectGenericPickup(PickupId, itemId, quantity);

            // Play local visual feedback right away
            if (audioSource != null && pickupSound != null)
            {
                audioSource.pitch = Random.Range(1.05f, 1.2f);
                audioSource.PlayOneShot(pickupSound);
            }

            ShrinkAndDestroy();
        }

        /// <summary>
        /// Called on all PEER clients (non-collectors) via RpcDespawnGenericPickup to
        /// destroy this pickup visually. Mirrors the RemoteCollect pattern in CoinPickup.
        /// </summary>
        public void RemoteCollect(Transform collectorTransform)
        {
            if (_isCollected) return;
            _isCollected = true;

            if (_triggerCollider != null) _triggerCollider.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

            ShrinkAndDestroy();
        }

        private void ShrinkAndDestroy()
        {
            if (visualModel != null)
            {
                Tween.Scale(visualModel, Vector3.zero, 0.15f, Ease.InBack)
                     .OnComplete(this, t => Destroy(t.gameObject));
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
