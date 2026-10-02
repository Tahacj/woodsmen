using UnityEngine;

namespace Woodsmen.Feedback
{
    /// <summary>
    /// Centralized, zero-allocation VFX pooling system (Rule 5).
    /// Pre-warms visual impact effects so they can be triggered from anywhere in the world
    /// without instantiating new GameObjects or requiring individual scene object wiring on environment props.
    /// Ensures simultaneous playback across both parent and child ParticleSystems (e.g. wood splinters + dust puff).
    /// </summary>
    public class VFXManager : MonoBehaviour
    {
        private static VFXManager _instance;
        public static VFXManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Object.FindFirstObjectByType<VFXManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("VFXManager");
                        _instance = go.AddComponent<VFXManager>();
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Wood Impact VFX")]
        [Tooltip("Prefab containing parent wood splinters and child dust puff ParticleSystems (e.g. FX_Impact_Wood_Ztest 8).")]
        [SerializeField] private GameObject woodImpactPrefab;

        [Tooltip("Number of pre-warmed instances kept ready in memory.")]
        [SerializeField] private int woodPoolSize = 6;

        private struct PooledVfx
        {
            public GameObject Root;
            public Transform Transform;
            public ParticleSystem RootParticle;
            public ParticleSystem[] AllParticles;
        }

        private PooledVfx[] _woodPool;
        private int _woodPoolIndex;
        private Transform _poolContainer;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            InitializePool();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void InitializePool()
        {
            if (woodImpactPrefab == null)
            {
#if UNITY_EDITOR
                woodImpactPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/VFX/FX_Impact_Wood_Ztest 8.prefab");
#endif
                if (woodImpactPrefab == null)
                {
                    woodImpactPrefab = Resources.Load<GameObject>("VFX/FX_Impact_Wood_Ztest 8");
                }
            }

            if (woodImpactPrefab == null)
            {
                Debug.LogWarning("[Woodsmen.VFXManager] No woodImpactPrefab assigned. Wood impact effects will be skipped.");
                return;
            }

            _poolContainer = new GameObject("VFX_Pool_Container").transform;
            _poolContainer.SetParent(transform);

            _woodPool = new PooledVfx[Mathf.Max(1, woodPoolSize)];

            for (int i = 0; i < _woodPool.Length; i++)
            {
                GameObject instance = Instantiate(woodImpactPrefab, _poolContainer);
                instance.name = $"{woodImpactPrefab.name}_Pooled_{i}";

                // Cache all components upfront for zero-GC runtime operations (Rule 5)
                var pooled = new PooledVfx
                {
                    Root = instance,
                    Transform = instance.transform,
                    RootParticle = instance.GetComponent<ParticleSystem>(),
                    AllParticles = instance.GetComponentsInChildren<ParticleSystem>(true)
                };

                // Stop all systems and deactivate
                for (int p = 0; p < pooled.AllParticles.Length; p++)
                {
                    pooled.AllParticles[p].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }

                instance.SetActive(false);
                _woodPool[i] = pooled;
            }

            _woodPoolIndex = 0;
        }

        /// <summary>
        /// Plays a pooled wood impact effect at the specified world position and orientation.
        /// Fires BOTH the parent particle system (wood chips) and all child particle systems (dust puffs).
        /// Fully non-allocating (Zero GC).
        /// </summary>
        /// <param name="position">World position of the impact point.</param>
        /// <param name="normal">Optional impact normal (defaults to Vector3.up if zero/default).</param>
        public void PlayWoodImpact(Vector3 position, Vector3 normal = default)
        {
            if (_woodPool == null || _woodPool.Length == 0) return;

            // Advance ring buffer index (Zero GC circular allocation)
            _woodPoolIndex = (_woodPoolIndex + 1) % _woodPool.Length;
            ref PooledVfx item = ref _woodPool[_woodPoolIndex];

            if (item.Root == null) return;

            // Position and orient
            item.Transform.position = position;

            if (normal != default && normal.sqrMagnitude > 0.001f)
            {
                item.Transform.rotation = Quaternion.LookRotation(normal);
            }

            // Ensure GameObject is active
            if (!item.Root.activeSelf)
            {
                item.Root.SetActive(true);
            }

            // Play both parent and all child ParticleSystems simultaneously
            if (item.RootParticle != null)
            {
                item.RootParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                item.RootParticle.Play(true); // withChildren = true triggers child dust puff!
            }
            else
            {
                for (int i = 0; i < item.AllParticles.Length; i++)
                {
                    item.AllParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    item.AllParticles[i].Play(false);
                }
            }
        }
    }
}
