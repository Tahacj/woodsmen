using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Woodsmen.Environment
{
    /// <summary>
    /// Spawns and manages destructible trees on a Terrain with configurable density,
    /// safe clearing zones, slope detection, Poisson-style anti-clumping, and organic variation.
    /// Supports both baked scene generation (Editor) and procedural generation (Runtime).
    /// </summary>
    [ExecuteAlways]
    public class TerrainTreeSpawner : MonoBehaviour
    {
        [Header("Target Terrain")]
        [Tooltip("The terrain to spawn trees on. If unassigned, automatically detects Terrain on this GameObject or in the scene.")]
        [SerializeField] private Terrain targetTerrain;

        [Header("Density & Prefab Palette")]
        [Tooltip("Total number of destructible trees to spawn on this terrain.")]
        [SerializeField, Min(1)] private int treeCount = 120;

        [Tooltip("Array of destructible tree prefabs (e.g. Tree_01, Tree_02, Tree_03, Tree_04, Tree_05). Randomly picked.")]
        [SerializeField] private GameObject[] treePrefabs;

        [Header("Placement Rules")]
        [Tooltip("Minimum distance in meters between any two trees to prevent clumping.")]
        [SerializeField, Range(1.5f, 15f)] private float minSpacing = 4.0f;

        [Tooltip("Center of the safe clearing zone (e.g. player spawn / camp) where trees will NOT spawn.")]
        [SerializeField] private Vector3 clearingCenter = new Vector3(-10f, 0f, -5f);

        [Tooltip("Radius around clearingCenter where no trees are spawned.")]
        [SerializeField, Range(0f, 50f)] private float clearingRadius = 14f;

        [Tooltip("Distance from terrain edges where trees will NOT spawn.")]
        [SerializeField, Range(0f, 30f)] private float edgePadding = 6f;

        [Tooltip("Maximum allowed slope angle in degrees. Prevents trees from spawning on vertical rock faces.")]
        [SerializeField, Range(10f, 75f)] private float maxSlopeAngle = 32f;

        [Header("Organic Variety")]
        [Tooltip("Random scale range (min, max) applied to spawned trees.")]
        [SerializeField] private Vector2 scaleRange = new Vector2(0.85f, 1.25f);

        [Tooltip("Apply random Y rotation (0-360 degrees) to each spawned tree.")]
        [SerializeField] private bool randomRotation = true;

        [Tooltip("Random seed for reproducible layouts. If 0, uses a random seed.")]
        [SerializeField] private int seed = 42;

        [Tooltip("Generate a new random seed every time you click Bake/Generate.")]
        [SerializeField] private bool randomizeSeedOnGenerate = true;

        [Header("Hierarchy & Lifecycle")]
        [Tooltip("Parent transform to hold spawned trees. If null, automatically created as child.")]
        [SerializeField] private Transform treesContainer;

        [Tooltip("If true, automatically spawns trees at scene start/awake. If false, relies on baked editor trees.")]
        [SerializeField] private bool spawnOnStart = false;

        public int TreeCount
        {
            get => treeCount;
            set => treeCount = Mathf.Max(1, value);
        }

        public GameObject[] TreePrefabs
        {
            get => treePrefabs;
            set => treePrefabs = value;
        }

        public int SpawnedTreeCount => treesContainer != null ? treesContainer.childCount : 0;
        public Terrain TargetTerrain => targetTerrain;
        public Transform TreesContainer => treesContainer;

        private void Awake()
        {
            EnsureTerrainReference();

            if (Application.isPlaying && spawnOnStart)
            {
                GenerateTrees();
            }
        }

        public void EnsureTerrainReference()
        {
            if (targetTerrain == null)
            {
                targetTerrain = GetComponent<Terrain>();
            }
            if (targetTerrain == null)
            {
                targetTerrain = Terrain.activeTerrain;
            }
        }

        public void EnsureContainer()
        {
            if (treesContainer == null)
            {
                Transform existing = transform.Find("[Spawned_Trees]");
                if (existing != null)
                {
                    treesContainer = existing;
                }
                else
                {
                    GameObject go = new GameObject("[Spawned_Trees]");
                    go.transform.SetParent(transform, false);
                    treesContainer = go.transform;
                }
            }
        }

        /// <summary>
        /// Clears all currently spawned trees under the container.
        /// </summary>
        public void ClearTrees()
        {
            EnsureContainer();

            if (treesContainer == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.RegisterFullObjectHierarchyUndo(treesContainer.gameObject, "Clear Spawned Trees");
            }
#endif

            List<GameObject> children = new List<GameObject>();
            for (int i = 0; i < treesContainer.childCount; i++)
            {
                children.Add(treesContainer.GetChild(i).gameObject);
            }

            foreach (var child in children)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    Undo.DestroyObjectImmediate(child);
                }
                else
                {
                    Destroy(child);
                }
#else
                Destroy(child);
#endif
            }

            Debug.Log("[Woodsmen] Cleared spawned trees.");
        }

        /// <summary>
        /// Generates the requested number of destructible trees on the terrain.
        /// </summary>
        [ContextMenu("Generate Trees")]
        public void GenerateTrees()
        {
            EnsureTerrainReference();
            if (targetTerrain == null)
            {
                Debug.LogError("[Woodsmen] TerrainTreeSpawner: No Terrain assigned or found!");
                return;
            }

            TerrainData tData = targetTerrain.terrainData;
            if (tData == null)
            {
                Debug.LogError("[Woodsmen] TerrainTreeSpawner: Target Terrain has no TerrainData!");
                return;
            }

            if (treePrefabs == null || treePrefabs.Length == 0)
            {
                Debug.LogWarning("[Woodsmen] TerrainTreeSpawner: No tree prefabs assigned in palette!");
                return;
            }

            EnsureContainer();
            ClearTrees();

            if (randomizeSeedOnGenerate && !Application.isPlaying)
            {
                seed = Random.Range(1, 999999);
            }

            Random.InitState(seed);

            Vector3 tPos = targetTerrain.transform.position;
            Vector3 tSize = tData.size;

            float minX = tPos.x + edgePadding;
            float maxX = tPos.x + tSize.x - edgePadding;
            float minZ = tPos.z + edgePadding;
            float maxZ = tPos.z + tSize.z - edgePadding;

            List<Vector3> placedPositions = new List<Vector3>(treeCount);
            float minSpacingSqr = minSpacing * minSpacing;
            float clearingRadiusSqr = clearingRadius * clearingRadius;

            int maxAttemptsPerTree = 50;
            int totalAttempts = 0;
            int maxTotalAttempts = treeCount * maxAttemptsPerTree;

            while (placedPositions.Count < treeCount && totalAttempts < maxTotalAttempts)
            {
                totalAttempts++;

                float rx = Random.Range(minX, maxX);
                float rz = Random.Range(minZ, maxZ);

                // 1. Check Safe Clearing Zone
                float dx = rx - clearingCenter.x;
                float dz = rz - clearingCenter.z;
                if ((dx * dx + dz * dz) < clearingRadiusSqr)
                {
                    continue;
                }

                // 2. Check Slope Angle (Steepness)
                float normX = (rx - tPos.x) / tSize.x;
                float normZ = (rz - tPos.z) / tSize.z;
                if (normX < 0f || normX > 1f || normZ < 0f || normZ > 1f)
                {
                    continue;
                }

                float steepness = tData.GetSteepness(normX, normZ);
                if (steepness > maxSlopeAngle)
                {
                    continue;
                }

                // 3. Check Minimum Spacing against previously placed trees
                bool tooClose = false;
                for (int i = 0; i < placedPositions.Count; i++)
                {
                    Vector3 prev = placedPositions[i];
                    float pdx = rx - prev.x;
                    float pdz = rz - prev.z;
                    if ((pdx * pdx + pdz * pdz) < minSpacingSqr)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose)
                {
                    continue;
                }

                // 4. Sample accurate terrain surface height
                float groundY = targetTerrain.SampleHeight(new Vector3(rx, 0f, rz)) + tPos.y;
                Vector3 worldPos = new Vector3(rx, groundY, rz);

                // 5. Pick random prefab
                GameObject prefab = treePrefabs[Random.Range(0, treePrefabs.Length)];
                if (prefab == null) continue;

                // 6. Instantiate with organic rotation and scale
                Quaternion rot = randomRotation ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) : Quaternion.identity;
                float scaleFactor = Random.Range(scaleRange.x, scaleRange.y);

                GameObject instance;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, treesContainer);
                    instance.transform.position = worldPos;
                    instance.transform.rotation = rot;
                    instance.transform.localScale = Vector3.one * scaleFactor;
                    Undo.RegisterCreatedObjectUndo(instance, "Spawn Destructible Tree");
                }
                else
                {
                    instance = Instantiate(prefab, worldPos, rot, treesContainer);
                    instance.transform.localScale = Vector3.one * scaleFactor;
                }
#else
                instance = Instantiate(prefab, worldPos, rot, treesContainer);
                instance.transform.localScale = Vector3.one * scaleFactor;
#endif

                placedPositions.Add(worldPos);
            }

            Debug.Log($"<color=#50fa7b><b>[Woodsmen]</b> Successfully spawned {placedPositions.Count} / {treeCount} destructible trees on '{targetTerrain.name}' (Seed: {seed}).</color>");
        }

        private void OnDrawGizmosSelected()
        {
            // Draw Clearing Safe Zone in Scene View
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            Gizmos.DrawSphere(clearingCenter, clearingRadius);
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(clearingCenter, clearingRadius);

            // Draw Terrain Spawn Area Bounds
            if (targetTerrain != null && targetTerrain.terrainData != null)
            {
                Vector3 tPos = targetTerrain.transform.position;
                Vector3 tSize = targetTerrain.terrainData.size;

                Vector3 boxCenter = new Vector3(tPos.x + tSize.x * 0.5f, tPos.y + tSize.y * 0.5f, tPos.z + tSize.z * 0.5f);
                Vector3 boxSize = new Vector3(Mathf.Max(0f, tSize.x - edgePadding * 2f), tSize.y, Mathf.Max(0f, tSize.z - edgePadding * 2f));

                Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.25f);
                Gizmos.DrawWireCube(boxCenter, boxSize);
            }
        }
    }
}
