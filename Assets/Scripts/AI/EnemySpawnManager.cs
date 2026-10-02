using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using Woodsmen.Combat;

namespace Woodsmen.AI
{
    /// <summary>
    /// Networked Enemy Spawn Manager & Object Pool.
    /// Features:
    /// - Pre-warmed object pooling (Zero-GC, eliminates runtime Instantiate/Destroy spikes, Rule 11).
    /// - Spawns enemies around active players on valid NavMesh locations.
    /// - Mirror multiplayer compliance: server-authoritative spawning with NetworkServer.Spawn / UnSpawn,
    ///   and 100% offline single-player fallback (Directive 12).
    /// - Automatic throttling and concurrent alive enemy limits for high performance.
    /// </summary>
    public class EnemySpawnManager : MonoBehaviour
    {
        private static EnemySpawnManager _instance;
        public static EnemySpawnManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<EnemySpawnManager>();
                }
                return _instance;
            }
        }

        [Header("Enemy Prefab")]
        [Tooltip("Prefab instantiated into the pool. Must have GoblinAI and CharacterHealth.")]
        [SerializeField] private GameObject enemyPrefab;

        [Header("Pool Configuration")]
        [Tooltip("Pre-warmed count of enemies created on startup.")]
        [SerializeField] private int initialPoolSize = 15;
        [Tooltip("Maximum allowed pool capacity.")]
        [SerializeField] private int maxPoolSize = 40;

        [Header("Spawn Settings")]
        [Tooltip("Whether automated timed spawning is currently active.")]
        [SerializeField] private bool autoSpawningEnabled = true;
        [Tooltip("Time interval in seconds between spawn attempts.")]
        [SerializeField] private float spawnInterval = 3.5f;
        [Tooltip("Maximum concurrent living enemies allowed in the scene at once.")]
        [SerializeField] private int maxAliveEnemies = 12;
        [Tooltip("Minimum distance from players to spawn enemies.")]
        [SerializeField] private float minSpawnRadius = 14f;
        [Tooltip("Maximum distance from players to spawn enemies.")]
        [SerializeField] private float maxSpawnRadius = 24f;

        // Object Pool State
        private readonly Queue<GoblinAI> _availablePool = new Queue<GoblinAI>();
        private readonly List<GoblinAI> _activeEnemies = new List<GoblinAI>();
        private Transform _poolContainer;
        private float _spawnTimer;

        public int ActiveEnemyCount => _activeEnemies.Count;
        public int AvailableEnemyCount => _availablePool.Count;

        /// <summary>
        /// True if this is the network server OR running in single-player offline mode (Directive 12).
        /// </summary>
        private bool IsAuthoritative => NetworkServer.active || (!NetworkClient.active && !NetworkServer.active);

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            LoadPrefabIfNull();
            // Note: InitializePool is intentionally deferred to Start().
            // Instantiating networked prefabs inside Awake() during scene load causes Mirror's
            // NetworkScenePostProcess to flag them as un-resaved scene objects with sceneId == 0,
            // which aborts Unity Editor play mode.
        }

        private void Start()
        {
            // Only server or offline single-player host needs to instantiate and manage the pool.
            if (IsAuthoritative)
            {
                InitializePool();
            }
        }

        private void OnDestroy()
        {
            if (_poolContainer != null)
            {
                Destroy(_poolContainer.gameObject);
            }
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void LoadPrefabIfNull()
        {
            if (enemyPrefab != null) return;

#if UNITY_EDITOR
            enemyPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Enemies/Goblin lvl1.prefab");
#endif
            if (enemyPrefab == null)
            {
                enemyPrefab = Resources.Load<GameObject>("Enemies/Goblin lvl1");
            }
        }

        private void InitializePool()
        {
            if (enemyPrefab == null)
            {
                Debug.LogWarning("[Woodsmen.EnemySpawnManager] No enemyPrefab assigned. Enemy spawning will be disabled.");
                return;
            }

            if (_poolContainer == null)
            {
                GameObject container = new GameObject("[Enemy_Pool_Container]");
                _poolContainer = container.transform;
            }

            for (int i = 0; i < initialPoolSize; i++)
            {
                CreatePooledInstance(i);
            }
        }

        private GoblinAI CreatePooledInstance(int index)
        {
            GameObject instance = Instantiate(enemyPrefab, _poolContainer);
            instance.name = $"{enemyPrefab.name}_Pooled_{index}";

            if (!instance.TryGetComponent(out GoblinAI ai))
            {
                ai = instance.AddComponent<GoblinAI>();
            }

            ai.OnDespawnRequested += DespawnEnemy;
            ai.OnDespawnToPool();

            _availablePool.Enqueue(ai);
            return ai;
        }

        private void Update()
        {
            if (!IsAuthoritative) return;
            if (!autoSpawningEnabled) return;

            _spawnTimer -= Time.deltaTime;
            if (_spawnTimer <= 0f)
            {
                _spawnTimer = spawnInterval;
                TrySpawnEnemy();
            }
        }

        /// <summary>
        /// Attempts to spawn an enemy at a valid NavMesh location around an active player.
        /// </summary>
        public GoblinAI TrySpawnEnemy()
        {
            if (_activeEnemies.Count >= maxAliveEnemies) return null;

            // Find an active player to spawn around
            Transform playerTarget = GetRandomActivePlayer();
            if (playerTarget == null) return null;

            const int maxSpawnAttempts = 12;
            Vector3 finalSpawnPos = Vector3.zero;
            bool foundValidPosition = false;

            for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
            {
                // Calculate random point in an annular ring around player
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(minSpawnRadius, maxSpawnRadius);
                Vector3 offset = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                Vector3 candidatePos = playerTarget.position + offset;

                // Sample NavMesh for a guaranteed walkable coordinate
                Vector3 spawnPos = candidatePos;
                if (NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                {
                    spawnPos = hit.position;
                }
                else
                {
                    // Fallback raycast to ground
                    if (Physics.Raycast(candidatePos + Vector3.up * 20f, Vector3.down, out RaycastHit groundHit, 40f))
                    {
                        spawnPos = groundHit.point;
                    }
                }

                // Check if candidate position is inside a prohibited / safe zone
                if (EnemySpawnExclusionZone.IsPointExcluded(spawnPos))
                {
                    continue; // Prohibited! Pick a different angle/distance
                }

                finalSpawnPos = spawnPos;
                foundValidPosition = true;
                break;
            }

            if (!foundValidPosition) return null;

            Vector3 dirToPlayer = playerTarget.position - finalSpawnPos;
            dirToPlayer.y = 0f;
            Quaternion spawnRot = dirToPlayer.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(dirToPlayer.normalized) * Quaternion.Euler(0f, 180f, 0f)
                : Quaternion.Euler(0f, 180f, 0f);

            return SpawnEnemyAt(finalSpawnPos, spawnRot);
        }

        /// <summary>
        /// Spawns an enemy instance at an exact world position and orientation.
        /// </summary>
        public GoblinAI SpawnEnemyAt(Vector3 position, Quaternion rotation)
        {
            GoblinAI enemy = null;

            while (_availablePool.Count > 0 && enemy == null)
            {
                enemy = _availablePool.Dequeue();
            }

            if (enemy == null)
            {
                if (_activeEnemies.Count + _availablePool.Count < maxPoolSize)
                {
                    enemy = CreatePooledInstance(_activeEnemies.Count + _availablePool.Count);
                    _availablePool.Dequeue();
                }
                else
                {
                    Debug.LogWarning("[Woodsmen.EnemySpawnManager] Pool capacity reached. Cannot spawn more enemies.");
                    return null;
                }
            }

            _activeEnemies.Add(enemy);
            enemy.transform.SetParent(null);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(enemy.gameObject, gameObject.scene);

            // Mirror networking: Spawn on server first so NetworkIdentity is active on the network
            if (NetworkServer.active && !enemy.netIdentity.isServer)
            {
                NetworkServer.Spawn(enemy.gameObject);
            }

            enemy.OnSpawnFromPool(position, rotation);

            Debug.Log($"<color=#50fa7b><b>[Woodsmen AI]</b> Spawned enemy '{enemy.gameObject.name}' at {position}. Active: {_activeEnemies.Count}/{maxAliveEnemies}</color>");
            return enemy;
        }

        /// <summary>
        /// Recycles an enemy back into the pool.
        /// </summary>
        public void DespawnEnemy(GoblinAI enemy)
        {
            if (enemy == null) return;

            _activeEnemies.Remove(enemy);

            // Mirror networking: Unspawn on server
            if (NetworkServer.active)
            {
                NetworkServer.UnSpawn(enemy.gameObject);
            }

            enemy.OnDespawnToPool();
            if (_poolContainer != null)
            {
                enemy.transform.SetParent(_poolContainer);
            }

            _availablePool.Enqueue(enemy);
        }

        /// <summary>
        /// Immediately despawns all currently active enemies.
        /// </summary>
        public void DespawnAll()
        {
            for (int i = _activeEnemies.Count - 1; i >= 0; i--)
            {
                DespawnEnemy(_activeEnemies[i]);
            }
        }

        private Transform GetRandomActivePlayer()
        {
            CharacterHealth[] allEntities = FindObjectsByType<CharacterHealth>(FindObjectsSortMode.None);
            var activePlayers = new List<Transform>(4);

            for (int i = 0; i < allEntities.Length; i++)
            {
                CharacterHealth entity = allEntities[i];
                if (entity == null || entity.IsDead) continue;

                if (entity.gameObject.CompareTag("Player") ||
                    entity.name.Contains("Lumberjack") ||
                    entity.name.Contains("Warrior") ||
                    entity.TryGetComponent(out Woodsmen.Players.LocomotionController _))
                {
                    activePlayers.Add(entity.transform);
                }
            }

            if (activePlayers.Count == 0) return null;
            return activePlayers[Random.Range(0, activePlayers.Count)];
        }
    }
}
