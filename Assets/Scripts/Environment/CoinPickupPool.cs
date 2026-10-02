using System.Collections.Generic;
using UnityEngine;

namespace Woodsmen.Environment
{
    /// <summary>
    /// High-performance, zero-allocation object pool for CoinPickup entities.
    /// Eliminates runtime Instantiate / Destroy garbage collection spikes during combat.
    /// Features:
    /// - Pre-warmed pool on scene startup.
    /// - Automatic fallback loading from Resources if unassigned in inspector.
    /// - Safe auto-spawning singleton accessible via CoinPickupPool.Instance.
    /// </summary>
    public class CoinPickupPool : MonoBehaviour
    {
        private static CoinPickupPool _instance;
        public static CoinPickupPool Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<CoinPickupPool>();
                    if (_instance == null)
                    {
                        GameObject poolObj = new GameObject("[CoinPickup_Pool]");
                        _instance = poolObj.AddComponent<CoinPickupPool>();
                    }
                }
                return _instance;
            }
        }

        [Header("Coin Prefab Reference")]
        [Tooltip("Prefab instantiated into the pool. Must have CoinPickup component.")]
        [SerializeField] private CoinPickup coinPickupPrefab;

        [Header("Pool Configuration")]
        [Tooltip("Number of coin instances pre-warmed on Awake.")]
        [SerializeField] private int initialPoolSize = 35;
        [Tooltip("Maximum allowed capacity for the object pool.")]
        [SerializeField] private int maxPoolSize = 100;

        [Header("Spawn Impulse Settings")]
        [SerializeField] private float upwardForceMin = 3.5f;
        [SerializeField] private float upwardForceMax = 5.5f;
        [SerializeField] private float minHorizontalForce = 1.2f;
        [SerializeField] private float maxHorizontalForce = 2.8f;
        [SerializeField] private float spawnHeightOffset = 0.5f;

        private static int _nextCoinId = 1;
        public static int GetNextCoinIdRange(int count)
        {
            int start = _nextCoinId;
            _nextCoinId += Mathf.Max(1, count);
            return start;
        }

        private readonly Dictionary<int, CoinPickup> _activeCoinsById = new Dictionary<int, CoinPickup>(64);
        // Tracks IDs claimed by ConsumeCoin so a second player can't collect the same coin.
        // Kept separate from _activeCoinsById so DespawnCoin can still find the pickup for visual removal.
        private readonly HashSet<int> _consumedCoinIds = new HashSet<int>();
        private readonly Queue<CoinPickup> _availablePool = new Queue<CoinPickup>();
        private Transform _poolContainer;

        public CoinPickup CoinPickupPrefab
        {
            get => coinPickupPrefab;
            set => coinPickupPrefab = value;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _poolContainer = transform;

            LoadPrefab();
            PrewarmPool();
        }

        private void Start()
        {
            EnsureHUDWired();
        }

        private void EnsureHUDWired()
        {
            // Auto-detect and wire Coins Canvas in the scene if not yet wired
            GameObject coinsCanvas = GameObject.Find("Coins Canvas");
            if (coinsCanvas == null)
            {
                Canvas[] allCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                foreach (var c in allCanvases)
                {
                    if (c.gameObject.name.Contains("Coin"))
                    {
                        coinsCanvas = c.gameObject;
                        break;
                    }
                }
            }

            if (coinsCanvas != null)
            {
                // Clean up any accidental WoodHUDCounter copied over from duplicating Wood Canvas
                var strayWoodCounters = coinsCanvas.GetComponentsInChildren<Woodsmen.Inventory.UI.WoodHUDCounter>(true);
                for (int i = 0; i < strayWoodCounters.Length; i++)
                {
                    Destroy(strayWoodCounters[i]);
                }

                if (coinsCanvas.GetComponentInChildren<Woodsmen.Inventory.UI.CoinHUDCounter>(true) == null)
                {
                    TMPro.TMP_Text tmp = coinsCanvas.GetComponentInChildren<TMPro.TMP_Text>(true);
                    GameObject target = tmp != null ? tmp.gameObject : coinsCanvas;
                    target.AddComponent<Woodsmen.Inventory.UI.CoinHUDCounter>();
                    Debug.Log($"<color=#50fa7b><b>[Woodsmen]</b> Automatically wired CoinHUDCounter to '{target.name}' under Coins Canvas!</color>");
                }
            }
        }

        private CoinPickup LoadPrefab()
        {
            if (coinPickupPrefab != null) return coinPickupPrefab;

            coinPickupPrefab = Resources.Load<CoinPickup>("Coin/Coin_Pickup");
            if (coinPickupPrefab == null)
            {
                coinPickupPrefab = Resources.Load<CoinPickup>("Items/coin/Coin_Pickup");
            }

            return coinPickupPrefab;
        }

        private void PrewarmPool()
        {
            if (coinPickupPrefab == null) return;

            for (int i = 0; i < initialPoolSize; i++)
            {
                CoinPickup pickup = InstantiateNewInstance();
                if (pickup != null)
                {
                    ReturnToPool(pickup);
                }
            }
        }

        private CoinPickup InstantiateNewInstance()
        {
            if (coinPickupPrefab == null)
            {
                LoadPrefab();
                if (coinPickupPrefab == null)
                {
                    return CreateProceduralCoinInstance();
                }
            }

            CoinPickup pickup = Instantiate(coinPickupPrefab, _poolContainer);
            pickup.name = $"{coinPickupPrefab.name}_Pooled";
            pickup.gameObject.SetActive(false);
            return pickup;
        }

        /// <summary>
        /// Spawns physical bouncing coins radially around an origin point.
        /// Capped at max 8 physical coins to keep physics lean and zero-GC.
        /// Total currency value is preserved across all spawned coins.
        /// </summary>
        public void SpawnCoins(Vector3 origin, int count)
        {
            SpawnCoins(origin, count, 0, 0);
        }

        /// <summary>
        /// Network-synchronized overload: spawns identical coins using a shared startCoinId and random seed.
        /// </summary>
        public void SpawnCoins(Vector3 origin, int count, int startCoinId, int seed)
        {
            if (count <= 0) return;

            if (coinPickupPrefab == null)
            {
                LoadPrefab();
                if (coinPickupPrefab == null)
                {
                    Debug.LogWarning("[CoinPickupPool] CoinPickupPrefab is missing and could not be loaded from Resources!");
                    return;
                }
            }

            Vector3 spawnCenter = origin + Vector3.up * spawnHeightOffset;

            // Cap physical visual instances to at most 8 for physics performance
            int visualCount = Mathf.Clamp(count, 1, 8);
            int baseValuePerPickup = count / visualCount;
            int remainder = count % visualCount;

            System.Random rng = seed != 0 ? new System.Random(seed) : new System.Random();

            float angleStep = 360f / Mathf.Max(1, visualCount);
            float baseAngleOffset = (float)(rng.NextDouble() * 360.0);

            for (int i = 0; i < visualCount; i++)
            {
                CoinPickup pickup = Get();
                if (pickup == null) break;

                int coinId = startCoinId > 0 ? (startCoinId + i) : 0;
                int thisValue = baseValuePerPickup + (i < remainder ? 1 : 0);

                pickup.CoinId = coinId;
                pickup.CoinValue = thisValue;

                if (coinId > 0)
                {
                    _activeCoinsById[coinId] = pickup;
                }

                // Calculate radial angle + slight random angle jitter using deterministic seed
                float jitter = (float)((rng.NextDouble() * 30.0) - 15.0);
                float currentAngle = baseAngleOffset + (angleStep * i) + jitter;
                float rad = currentAngle * Mathf.Deg2Rad;
                Vector3 horizontalDir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;

                // Calculate random launch impulse (radial outward + upward pop)
                float hForce = Mathf.Lerp(minHorizontalForce, maxHorizontalForce, (float)rng.NextDouble());
                float vForce = Mathf.Lerp(upwardForceMin, upwardForceMax, (float)rng.NextDouble());
                Vector3 launchImpulse = horizontalDir * hForce + Vector3.up * vForce;

                // Radial position offset so coins spawn cleanly outside dead body
                float posOffset = Mathf.Lerp(0.25f, 0.5f, (float)rng.NextDouble());
                Vector3 spawnPos = spawnCenter + horizontalDir * posOffset;

                pickup.Launch(spawnPos, launchImpulse, this);
            }
        }

        /// <summary>
        /// Retrieves an available CoinPickup from the pool or allocates a new one if exhausted.
        /// </summary>
        public CoinPickup Get()
        {
            CoinPickup pickup = null;

            while (_availablePool.Count > 0 && pickup == null)
            {
                pickup = _availablePool.Dequeue();
            }

            if (pickup == null)
            {
                if (_poolContainer.childCount < maxPoolSize)
                {
                    pickup = InstantiateNewInstance();
                }
                else
                {
                    Debug.LogWarning("[CoinPickupPool] Maximum coin pool capacity reached! Re-using oldest.");
                    return null;
                }
            }

            return pickup;
        }

        /// <summary>
        /// Returns a collected CoinPickup back into the pool.
        /// </summary>
        public void ReturnToPool(CoinPickup pickup)
        {
            if (pickup == null) return;

            if (pickup.CoinId > 0)
            {
                _activeCoinsById.Remove(pickup.CoinId);
                _consumedCoinIds.Remove(pickup.CoinId);
            }

            pickup.gameObject.SetActive(false);
            pickup.transform.SetParent(_poolContainer);
            _availablePool.Enqueue(pickup);
        }

        /// <summary>
        /// Atomically claims a coin so only the first player to call this gets it.
        /// Does NOT remove from the active-coin lookup so DespawnCoin can still find
        /// the pickup instance for visual removal on all clients.
        /// </summary>
        public bool ConsumeCoin(int coinId)
        {
            if (coinId <= 0) return true; // untracked offline coins always succeed
            // Coin must be in the active pool AND not already claimed
            if (_activeCoinsById.ContainsKey(coinId) && _consumedCoinIds.Add(coinId))
            {
                return true; // this caller wins the coin
            }
            return false; // already claimed or unknown ID
        }

        /// <summary>
        /// Despawns an active coin by its network-assigned ID, animating it toward the collecting player.
        /// Removes the coin from all tracking structures so it cannot be collected again.
        /// </summary>
        public void DespawnCoin(int coinId, Transform collectorTransform)
        {
            if (coinId <= 0) return;
            if (_activeCoinsById.TryGetValue(coinId, out CoinPickup pickup))
            {
                _activeCoinsById.Remove(coinId);
                _consumedCoinIds.Remove(coinId);
                if (pickup != null && pickup.gameObject.activeInHierarchy && !pickup.IsCollected)
                {
                    pickup.RemoteCollect(collectorTransform);
                }
            }
        }

        private CoinPickup CreateProceduralCoinInstance()
        {
            GameObject coinGo = new GameObject("Coin_Pickup_Procedural");
            coinGo.transform.SetParent(_poolContainer);

            Rigidbody rb = coinGo.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.linearDamping = 1.2f;
            rb.angularDamping = 2.0f;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            SphereCollider physCol = coinGo.AddComponent<SphereCollider>();
            physCol.isTrigger = false;
            physCol.radius = 0.18f;
            physCol.center = new Vector3(0f, 0.18f, 0f);

            SphereCollider triggerCol = coinGo.AddComponent<SphereCollider>();
            triggerCol.isTrigger = true;
            triggerCol.radius = 0.95f;
            triggerCol.center = new Vector3(0f, 0.18f, 0f);

            GameObject visualGo = new GameObject("Visual");
            visualGo.transform.SetParent(coinGo.transform, false);
            visualGo.transform.localPosition = new Vector3(0f, 0.20f, 0f);

            GameObject coinModel = Resources.Load<GameObject>("Coin/source/Coin");
            Mesh coinMesh = null;
            if (coinModel != null)
            {
                MeshFilter mf = coinModel.GetComponentInChildren<MeshFilter>();
                if (mf != null) coinMesh = mf.sharedMesh;
            }

            MeshFilter visualMf = visualGo.AddComponent<MeshFilter>();
            if (coinMesh != null)
            {
                visualMf.sharedMesh = coinMesh;
                float maxDim = Mathf.Max(coinMesh.bounds.size.x, coinMesh.bounds.size.y, coinMesh.bounds.size.z);
                visualGo.transform.localScale = Vector3.one * (maxDim > 0.001f ? 0.45f / maxDim : 0.5f);
            }
            else
            {
                GameObject tempCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                visualMf.sharedMesh = tempCylinder.GetComponent<MeshFilter>().sharedMesh;
                visualGo.transform.localScale = new Vector3(0.35f, 0.05f, 0.35f);
                Destroy(tempCylinder);
            }

            MeshRenderer visualMr = visualGo.AddComponent<MeshRenderer>();
            Material goldMat = Resources.Load<Material>("Coin/Coin_Gold");
            if (goldMat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                goldMat = new Material(shader);
                if (goldMat.HasProperty("_BaseColor")) goldMat.SetColor("_BaseColor", new Color(1f, 0.82f, 0.16f, 1f));
                else if (goldMat.HasProperty("_Color")) goldMat.SetColor("_Color", new Color(1f, 0.82f, 0.16f, 1f));
                if (goldMat.HasProperty("_Metallic")) goldMat.SetFloat("_Metallic", 0.85f);
                if (goldMat.HasProperty("_Smoothness")) goldMat.SetFloat("_Smoothness", 0.75f);
            }
            visualMr.sharedMaterial = goldMat;

            CoinPickup pickup = coinGo.AddComponent<CoinPickup>();
            pickup.CoinValue = 1;
            pickup.gameObject.SetActive(false);
            return pickup;
        }
    }
}
