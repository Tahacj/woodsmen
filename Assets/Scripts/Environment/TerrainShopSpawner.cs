using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Woodsmen.Environment
{
    /// <summary>
    /// Spawns interactive Shop prefabs on the terrain at IDENTICAL positions on every client.
    ///
    /// Design note
    /// ──────────
    /// NetworkServer.Spawn() only works for prefabs pre-registered in the NetworkManager
    /// spawnable prefabs list. Because that can be missed, we use a seeded local-spawn approach:
    ///   1. Server picks a random seed (OnStartServer).
    ///   2. Server broadcasts the seed to ALL clients via ClientRpc.
    ///   3. Every machine runs the same deterministic algorithm → identical shop positions.
    /// No NetworkIdentity is needed on the shop prefab.
    /// </summary>
    public class TerrainShopSpawner : NetworkBehaviour
    {
        [Header("Target Terrain")]
        [Tooltip("The terrain to spawn shops on. If unassigned, automatically detects Terrain on this GameObject.")]
        [SerializeField] private Terrain targetTerrain;

        [Header("Shop Settings")]
        [Tooltip("The Shop prefab to spawn. Does NOT require a NetworkIdentity.")]
        [SerializeField] private GameObject shopPrefab;

        [Tooltip("How many shops to spawn on this specific terrain.")]
        [SerializeField, Min(1)] private int shopCount = 3;

        [Tooltip("Minimum distance between each shop.")]
        [SerializeField, Range(10f, 200f)] private float minSpacing = 50f;

        [Tooltip("Distance from terrain edges where shops will NOT spawn.")]
        [SerializeField, Range(0f, 100f)] private float edgePadding = 20f;

        [Tooltip("Maximum allowed slope angle in degrees. Prevents shops from spawning on steep cliffs.")]
        [SerializeField, Range(0f, 45f)] private float maxSlopeAngle = 15f;

        [Header("Tree Clearing")]
        [Tooltip("Radius around the shop where trees will be automatically deleted so they don't overlap.")]
        [SerializeField, Range(0f, 30f)] private float treeClearRadius = 15f;

        // Prevents double-spawn on the host (which is both server and client)
        private bool _hasSpawned = false;

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Generate a seed and broadcast it to all clients (including this host as a client)
            int seed = UnityEngine.Random.Range(1, int.MaxValue);
            RpcSpawnShopsWithSeed(seed);
        }

        /// <summary>
        /// Runs on ALL clients (including host) with the SAME seed so every machine
        /// places shops at identical world positions.
        /// </summary>
        [ClientRpc]
        private void RpcSpawnShopsWithSeed(int seed)
        {
            if (_hasSpawned) return;
            _hasSpawned = true;
            GenerateShops(seed);
        }

        private void GenerateShops(int seed)
        {
            if (targetTerrain == null)
            {
                targetTerrain = GetComponent<Terrain>();
            }

            if (targetTerrain == null || shopPrefab == null)
            {
                Debug.LogError("[Woodsmen] TerrainShopSpawner: Missing Terrain or Shop Prefab.");
                return;
            }

            // Use System.Random seeded so ALL clients produce identical positions
            System.Random rng = new System.Random(seed);

            TerrainData tData = targetTerrain.terrainData;
            Vector3 tPos = targetTerrain.transform.position;
            Vector3 tSize = tData.size;

            float minX = tPos.x + edgePadding;
            float maxX = tPos.x + tSize.x - edgePadding;
            float minZ = tPos.z + edgePadding;
            float maxZ = tPos.z + tSize.z - edgePadding;

            List<Vector3> placedPositions = new List<Vector3>(shopCount);
            float minSpacingSqr = minSpacing * minSpacing;

            int maxTotalAttempts = shopCount * 100;
            int totalAttempts = 0;

            while (placedPositions.Count < shopCount && totalAttempts < maxTotalAttempts)
            {
                totalAttempts++;

                float rx = minX + (float)(rng.NextDouble() * (maxX - minX));
                float rz = minZ + (float)(rng.NextDouble() * (maxZ - minZ));

                // Check Slope Angle (Steepness)
                float normX = (rx - tPos.x) / tSize.x;
                float normZ = (rz - tPos.z) / tSize.z;
                if (normX < 0f || normX > 1f || normZ < 0f || normZ > 1f) continue;

                float steepness = tData.GetSteepness(normX, normZ);
                if (steepness > maxSlopeAngle) continue;

                // Check Minimum Spacing
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

                if (tooClose) continue;

                // Sample accurate terrain surface height
                float groundY = targetTerrain.SampleHeight(new Vector3(rx, 0f, rz)) + tPos.y;
                Vector3 worldPos = new Vector3(rx, groundY, rz);

                // Rotation is deterministic from the same seed
                float rotY = (float)(rng.NextDouble() * 360.0);
                Quaternion rot = Quaternion.Euler(0f, rotY, 0f);

                // Local instantiation — no NetworkServer.Spawn because every client runs this
                // exact code with the same seed, so they all get identical positions.
                Instantiate(shopPrefab, worldPos, rot);

                placedPositions.Add(worldPos);
            }

            Debug.Log($"<color=#f1fa8c><b>[Woodsmen]</b> TerrainShopSpawner spawned {placedPositions.Count}/{shopCount} shops (seed={seed}).</color>");

            // Clear trees around shops — runs on this machine (server and client both do it)
            foreach (var pos in placedPositions)
            {
                ClearTreesAround(pos);
            }
        }

        private void ClearTreesAround(Vector3 position)
        {
            Collider[] hits = Physics.OverlapSphere(position, treeClearRadius);
            foreach (Collider c in hits)
            {
                if (c.gameObject.name.Contains("Tree"))
                {
                    Destroy(c.gameObject);
                }
            }
        }
    }
}
