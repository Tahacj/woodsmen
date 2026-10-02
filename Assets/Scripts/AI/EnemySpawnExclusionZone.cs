using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Woodsmen.AI
{
    /// <summary>
    /// Designates a protected / safe area where enemies are strictly forbidden to spawn or enter.
    /// Used for player preparation camps, spawn squares, safe houses, towns, etc.
    /// Features:
    /// - Zero-GC static registry for instantaneous lookup by EnemySpawnManager.
    /// - Supports Box and Sphere shapes with full 3D rotation and scaling support.
    /// - Optional physical barrier / push-back to prevent roaming enemies from walking in.
    /// - Optional NavMesh carving to route enemies around the perimeter.
    /// - Visual 3D scene Gizmos with editable bounds.
    /// </summary>
    [SelectionBase]
    [ExecuteAlways]
    public class EnemySpawnExclusionZone : MonoBehaviour
    {
        public enum ExclusionShape
        {
            Box,
            Sphere
        }

        private static readonly List<EnemySpawnExclusionZone> ActiveZones = new List<EnemySpawnExclusionZone>();

        /// <summary>
        /// Global check to determine if a candidate world position is inside ANY active exclusion zone.
        /// Zero allocations.
        /// </summary>
        public static bool IsPointExcluded(Vector3 worldPoint)
        {
            for (int i = 0; i < ActiveZones.Count; i++)
            {
                EnemySpawnExclusionZone zone = ActiveZones[i];
                if (zone != null && zone.isActiveAndEnabled && zone.ContainsPoint(worldPoint))
                {
                    return true;
                }
            }
            return false;
        }

        [Header("Shape & Dimensions")]
        [SerializeField] private ExclusionShape shape = ExclusionShape.Box;
        [SerializeField] private Vector3 center = Vector3.zero;
        [SerializeField] private Vector3 boxSize = new Vector3(25f, 10f, 25f);
        [SerializeField] private float sphereRadius = 15f;

        [Header("Enemy Containment & Barrier")]
        [Tooltip("If true, enemies (Goblins) attempting to walk into this zone will be pushed back outside the perimeter.")]
        [SerializeField] private bool preventEnemyEntry = true;

        [Tooltip("If true, automatically ensures a NavMeshObstacle is attached and carves a hole in the NavMesh so enemies path around.")]
        [SerializeField] private bool carveNavMesh = false;

        public ExclusionShape Shape
        {
            get => shape;
            set => shape = value;
        }

        public Vector3 BoxSize
        {
            get => boxSize;
            set => boxSize = value;
        }

        public Vector3 Center
        {
            get => center;
            set => center = value;
        }

        public float SphereRadius
        {
            get => sphereRadius;
            set => sphereRadius = Mathf.Max(0.5f, value);
        }

        private void OnEnable()
        {
            if (!ActiveZones.Contains(this))
            {
                ActiveZones.Add(this);
            }

            SyncComponents();
        }

        private void OnDisable()
        {
            ActiveZones.Remove(this);
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                SyncComponents();
            }
        }

        private void SyncComponents()
        {
            // Ensure trigger collider exists for physical barrier
            if (preventEnemyEntry)
            {
                if (shape == ExclusionShape.Box)
                {
                    BoxCollider box = GetComponent<BoxCollider>();
                    if (box == null) box = gameObject.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.center = center;
                    box.size = boxSize;
                }
                else
                {
                    SphereCollider sphere = GetComponent<SphereCollider>();
                    if (sphere == null) sphere = gameObject.AddComponent<SphereCollider>();
                    sphere.isTrigger = true;
                    sphere.center = center;
                    sphere.radius = sphereRadius;
                }
            }

            // Sync NavMeshObstacle carving if enabled
            if (carveNavMesh)
            {
                NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
                if (obstacle == null) obstacle = gameObject.AddComponent<NavMeshObstacle>();
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
                if (shape == ExclusionShape.Box)
                {
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.center = center;
                    obstacle.size = boxSize;
                }
                else
                {
                    obstacle.shape = NavMeshObstacleShape.Capsule;
                    obstacle.center = center;
                    obstacle.radius = sphereRadius;
                    obstacle.height = boxSize.y;
                }
            }
        }

        /// <summary>
        /// Checks if a world position falls inside this zone's boundaries.
        /// Fully handles translation, rotation, and scaling.
        /// </summary>
        public bool ContainsPoint(Vector3 worldPoint)
        {
            if (shape == ExclusionShape.Box)
            {
                Vector3 local = transform.InverseTransformPoint(worldPoint) - center;
                Vector3 half = boxSize * 0.5f;

                return Mathf.Abs(local.x) <= half.x &&
                       Mathf.Abs(local.y) <= half.y &&
                       Mathf.Abs(local.z) <= half.z;
            }
            else
            {
                Vector3 worldCenter = transform.TransformPoint(center);
                float sqrDist = (worldPoint - worldCenter).sqrMagnitude;
                return sqrDist <= (sphereRadius * sphereRadius);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (!preventEnemyEntry || !Application.isPlaying) return;

            // Detect enemy entities
            if (other.TryGetComponent(out GoblinAI goblin) || other.GetComponentInParent<GoblinAI>() != null)
            {
                Transform enemyTransform = (goblin != null ? goblin.transform : other.transform.root);
                Vector3 worldCenter = transform.TransformPoint(center);
                Vector3 pushDirection = enemyTransform.position - worldCenter;
                pushDirection.y = 0f;

                if (pushDirection.sqrMagnitude < 0.01f)
                {
                    pushDirection = transform.forward;
                }
                pushDirection.Normalize();

                if (enemyTransform.TryGetComponent(out NavMeshAgent agent) && agent.isOnNavMesh)
                {
                    agent.velocity = pushDirection * 6f;
                    agent.Move(pushDirection * 3f * Time.deltaTime);
                }
                else
                {
                    enemyTransform.position += pushDirection * 3f * Time.deltaTime;
                }
            }
        }

        private void OnDrawGizmos()
        {
            Color fill = new Color(0.2f, 0.8f, 1f, 0.15f); // Soft cyan protective field
            Color wire = new Color(0.2f, 0.9f, 1f, 0.85f);

            Gizmos.matrix = transform.localToWorldMatrix;

            if (shape == ExclusionShape.Box)
            {
                Gizmos.color = fill;
                Gizmos.DrawCube(center, boxSize);
                Gizmos.color = wire;
                Gizmos.DrawWireCube(center, boxSize);
            }
            else
            {
                Gizmos.color = fill;
                Gizmos.DrawSphere(center, sphereRadius);
                Gizmos.color = wire;
                Gizmos.DrawWireSphere(center, sphereRadius);
            }

            #if UNITY_EDITOR
            Vector3 worldCenter = transform.TransformPoint(center);
            UnityEditor.Handles.color = wire;
            UnityEditor.Handles.Label(worldCenter + Vector3.up * (boxSize.y * 0.5f + 0.5f), "SAFE ZONE (No Enemies)");
            #endif
        }
    }
}
