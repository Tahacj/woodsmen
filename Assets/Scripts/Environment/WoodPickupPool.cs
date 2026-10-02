using System.Collections.Generic;
using UnityEngine;

namespace Woodsmen.Environment
{
    /// <summary>
    /// High-performance Object Pool for dropped wood resource items.
    /// Eliminates garbage collection and allocation spikes when felling trees.
    /// Manages radial dispersion physics when spawning multiple wood pickups.
    /// </summary>
    public class WoodPickupPool : MonoBehaviour
    {
        private static WoodPickupPool _instance;

        public static WoodPickupPool Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<WoodPickupPool>();
                    if (_instance == null)
                    {
                        GameObject poolObj = new GameObject("[WoodPickup_Pool]");
                        _instance = poolObj.AddComponent<WoodPickupPool>();
                    }
                }
                return _instance;
            }
        }

        [Header("Prefab Reference")]
        [Tooltip("Prefab instantiated into the pool. Must have WoodPickup component.")]
        [SerializeField] private WoodPickup woodPickupPrefab;

        [Header("Pool Configuration")]
        [Tooltip("Pre-warmed count of wood pickups created on start.")]
        [SerializeField] private int initialPoolSize = 35;

        [Tooltip("Maximum allowed pool size before discarding items.")]
        [SerializeField] private int maxPoolSize = 120;

        [Header("Dispersion Physics Settings")]
        [Tooltip("Minimum horizontal launch speed.")]
        [SerializeField] private float minHorizontalForce = 2.5f;

        [Tooltip("Maximum horizontal launch speed.")]
        [SerializeField] private float maxHorizontalForce = 4.2f;

        [Tooltip("Upward pop force to give drops an arc.")]
        [SerializeField] private float upwardForceMin = 4.5f;
        [SerializeField] private float upwardForceMax = 6.5f;

        [Tooltip("Vertical spawn offset above the tree center.")]
        [SerializeField] private float spawnHeightOffset = 0.5f;

        private readonly Queue<WoodPickup> _availablePool = new Queue<WoodPickup>();
        private readonly Dictionary<int, WoodPickup> _activeWoodById = new Dictionary<int, WoodPickup>();
        // Claimed IDs — separate from _activeWoodById so DespawnWood can still find the pickup
        // for visual removal on the host after ConsumeWood has already run server-side.
        private readonly HashSet<int> _consumedWoodIds = new HashSet<int>();
        private int _activeCount;
        private static int _nextWoodIdCounter = 1;

        /// <summary>
        /// Generates a sequential, globally unique block of Wood IDs on the server.
        /// </summary>
        public static int GetNextWoodIdRange(int count)
        {
            int start = _nextWoodIdCounter;
            _nextWoodIdCounter += count;
            return start;
        }

        public int ActiveCount => _activeCount;

        public WoodPickup WoodPickupPrefab
        {
            get => woodPickupPrefab;
            set => woodPickupPrefab = value;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            LoadPrefab();
            Prewarm();
        }

        private WoodPickup LoadPrefab()
        {
            if (woodPickupPrefab != null) return woodPickupPrefab;

            woodPickupPrefab = Resources.Load<WoodPickup>("Items/wood/Wood_Pickup");
            if (woodPickupPrefab == null)
            {
                GameObject go = Resources.Load<GameObject>("Items/wood/Wood_Pickup");
                if (go != null) woodPickupPrefab = go.GetComponent<WoodPickup>();
            }

            return woodPickupPrefab;
        }

        private void Prewarm()
        {
            if (LoadPrefab() == null) return;

            for (int i = 0; i < initialPoolSize; i++)
            {
                WoodPickup pickup = InstantiateNewInstance();
                if (pickup != null) _availablePool.Enqueue(pickup);
            }
        }

        private WoodPickup InstantiateNewInstance()
        {
            if (LoadPrefab() == null) return null;

            WoodPickup pickup = Instantiate(woodPickupPrefab, transform);
            pickup.gameObject.SetActive(false);
            return pickup;
        }

        public void SpawnWood(Vector3 origin, int count)
        {
            SpawnWood(origin, count, 0, 0);
        }

        /// <summary>
        /// Network-synchronized overload: spawns identical wood using a shared startWoodId and random seed.
        /// </summary>
        public void SpawnWood(Vector3 origin, int count, int startWoodId, int seed)
        {
            if (count <= 0) return;

            // Ensure prefab is loaded
            if (LoadPrefab() == null)
            {
                Debug.LogWarning("[WoodPickupPool] WoodPickupPrefab is missing and could not be loaded from Resources/Items/wood/Wood_Pickup!");
                return;
            }

            Vector3 spawnCenter = origin + Vector3.up * spawnHeightOffset;

            // Cap physical instances to at most 10 for performance, while precisely preserving exact total value
            int visualCount = Mathf.Clamp(count, 1, 10);
            int baseValuePerPickup = count / visualCount;
            int remainder = count % visualCount;

            // Save old state and seed deterministic random generator for identical layout on all clients
            Random.State oldState = Random.state;
            if (seed != 0) Random.InitState(seed);

            float angleStep = 360f / Mathf.Max(1, visualCount);
            float baseAngleOffset = Random.Range(0f, 360f);

            for (int i = 0; i < visualCount; i++)
            {
                WoodPickup pickup = Get();
                if (pickup == null) break;

                int thisValue = baseValuePerPickup + (i < remainder ? 1 : 0);
                pickup.WoodValue = thisValue;

                int assignedId = (startWoodId > 0) ? (startWoodId + i) : 0;
                pickup.WoodId = assignedId;

                if (assignedId > 0)
                {
                    _activeWoodById[assignedId] = pickup;
                }

                // Calculate radial angle + slight random angle jitter
                float currentAngle = baseAngleOffset + (angleStep * i) + Random.Range(-15f, 15f);
                float rad = currentAngle * Mathf.Deg2Rad;
                Vector3 horizontalDir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;

                // Calculate random launch impulse (radial outward + upward pop)
                float hForce = Random.Range(minHorizontalForce, maxHorizontalForce);
                float vForce = Random.Range(upwardForceMin, upwardForceMax);
                Vector3 launchImpulse = horizontalDir * hForce + Vector3.up * vForce;

                // Radial position offset so items spawn cleanly outside colliders
                Vector3 spawnPos = spawnCenter + horizontalDir * Random.Range(0.35f, 0.6f);

                pickup.Launch(spawnPos, launchImpulse, this);
            }

            if (seed != 0) Random.state = oldState;
        }

        /// <summary>
        /// Retrieves an available WoodPickup from the pool or allocates a new one if pool is exhausted.
        /// </summary>
        public WoodPickup Get()
        {
            WoodPickup pickup = null;

            while (_availablePool.Count > 0 && pickup == null)
            {
                pickup = _availablePool.Dequeue();
            }

            if (pickup == null)
            {
                pickup = InstantiateNewInstance();
                if (pickup == null) return null;
            }

            _activeCount++;
            return pickup;
        }

        /// <summary>
        /// Returns a collected WoodPickup back into the pool.
        /// </summary>
        public void ReturnToPool(WoodPickup pickup)
        {
            if (pickup == null) return;

            if (pickup.WoodId > 0)
            {
                _activeWoodById.Remove(pickup.WoodId);
                _consumedWoodIds.Remove(pickup.WoodId);
            }

            if (_activeCount > 0) _activeCount--;

            if (_availablePool.Count < maxPoolSize)
            {
                pickup.transform.SetParent(transform);
                pickup.gameObject.SetActive(false);
                _availablePool.Enqueue(pickup);
            }
            else
            {
                Destroy(pickup.gameObject);
            }
        }

        /// <summary>
        /// Atomically claims a wood pickup so only the first player wins it.
        /// Does NOT remove from the active-wood lookup so DespawnWood can still
        /// find the pickup instance for visual removal on all clients.
        /// </summary>
        public bool ConsumeWood(int woodId)
        {
            if (woodId <= 0) return true; // untracked offline wood always succeeds
            if (_activeWoodById.ContainsKey(woodId) && _consumedWoodIds.Add(woodId))
            {
                return true; // this caller wins the wood
            }
            return false; // already claimed or unknown ID
        }

        /// <summary>
        /// Forces a specific networked wood to despawn (called via RpcDespawnWood when a teammate collects it).
        /// Removes from all tracking structures so it cannot be re-collected.
        /// </summary>
        public void DespawnWood(int woodId, Transform collectorTransform)
        {
            if (_activeWoodById.TryGetValue(woodId, out WoodPickup pickup))
            {
                _activeWoodById.Remove(woodId);
                _consumedWoodIds.Remove(woodId);
                pickup.RemoteCollect(collectorTransform);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
