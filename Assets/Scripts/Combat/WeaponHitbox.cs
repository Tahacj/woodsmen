using UnityEngine;

namespace Woodsmen.Combat
{
    /// <summary>
    /// Geometric shape of the weapon hitbox.
    /// </summary>
    public enum HitboxShape
    {
        Sphere,
        Box
    }

    /// <summary>
    /// Universal, zero-allocation weapon hitbox component.
    /// Can be attached to any weapon prefab, tool bone (e.g. Axe head), or character socket.
    /// Provides customizable shapes (Sphere / Box), offsets, rotation, and live Scene-view Gizmos.
    /// </summary>
    [ExecuteAlways]
    public class WeaponHitbox : MonoBehaviour
    {
        [Header("Shape & Dimensions")]
        [Tooltip("Geometry used for physics overlap queries.")]
        [SerializeField] private HitboxShape shape = HitboxShape.Sphere;

        [Tooltip("Radius of the sphere hitbox (when Shape is Sphere).")]
        [SerializeField] private float radius = 0.8f;

        [Tooltip("Full size (width, height, depth) of the box hitbox (when Shape is Box).")]
        [SerializeField] private Vector3 boxSize = new Vector3(0.8f, 0.8f, 1.2f);

        [Header("Offsets & Alignment")]
        [Tooltip("Local position offset relative to this Transform.")]
        [SerializeField] private Vector3 centerOffset = Vector3.zero;

        [Tooltip("Local Euler rotation offset relative to this Transform (useful for orienting box hitboxes).")]
        [SerializeField] private Vector3 rotationOffset = Vector3.zero;

        [Header("Filtering")]
        [Tooltip("Layers that this hitbox can interact with / detect.")]
        [SerializeField] private LayerMask targetLayer = ~0;

        [Header("Scene Visualization")]
        [Tooltip("Draw gizmos in the Unity Scene View.")]
        [SerializeField] private bool showGizmo = true;

        [Tooltip("Only draw gizmos when this object or parent character is selected.")]
        [SerializeField] private bool onlyWhenSelected = false;

        [SerializeField] private Color inactiveColor = new Color(0f, 0.9f, 0.3f, 0.25f);
        [SerializeField] private Color activeColor = new Color(1f, 0.15f, 0.15f, 0.55f);

        private bool _isActive;

        #region Public Properties & API

        public HitboxShape Shape { get => shape; set => shape = value; }
        public float Radius { get => radius; set => radius = Mathf.Max(0.01f, value); }
        public Vector3 BoxSize { get => boxSize; set => boxSize = value; }
        public Vector3 CenterOffset { get => centerOffset; set => centerOffset = value; }
        public Vector3 RotationOffset { get => rotationOffset; set => rotationOffset = value; }
        public LayerMask TargetLayer { get => targetLayer; set => targetLayer = value; }
        public bool IsActive => _isActive;

        /// <summary>
        /// World-space center point of the hitbox taking local offsets into account.
        /// </summary>
        public Vector3 WorldCenter => transform.TransformPoint(centerOffset);

        /// <summary>
        /// World-space orientation of the hitbox taking local rotation offsets into account.
        /// </summary>
        public Quaternion WorldRotation => transform.rotation * Quaternion.Euler(rotationOffset);

        /// <summary>
        /// Half-extents vector for Physics.OverlapBox queries.
        /// </summary>
        public Vector3 BoxHalfExtents => boxSize * 0.5f;

        /// <summary>
        /// Enables or disables the hitbox detection state (typically called from animation events).
        /// </summary>
        public void SetActive(bool active)
        {
            _isActive = active;
        }

        /// <summary>
        /// Performs a zero-allocation overlap query against the physics world.
        /// </summary>
        /// <param name="resultsBuffer">Pre-allocated collider buffer to store detected colliders.</param>
        /// <returns>Number of colliders detected.</returns>
        public int CheckOverlap(Collider[] resultsBuffer)
        {
            if (resultsBuffer == null || resultsBuffer.Length == 0) return 0;

            if (shape == HitboxShape.Sphere)
            {
                return Physics.OverlapSphereNonAlloc(WorldCenter, radius, resultsBuffer, targetLayer);
            }
            else
            {
                return Physics.OverlapBoxNonAlloc(WorldCenter, BoxHalfExtents, resultsBuffer, WorldRotation, targetLayer);
            }
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmos()
        {
            if (!showGizmo || onlyWhenSelected) return;
            DrawHitboxGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmo || !onlyWhenSelected) return;
            DrawHitboxGizmo();
        }

        private void DrawHitboxGizmo()
        {
            Color fillColor = _isActive ? activeColor : inactiveColor;
            Color wireColor = new Color(fillColor.r, fillColor.g, fillColor.b, 0.95f);

            Vector3 center = WorldCenter;
            Quaternion rotation = WorldRotation;

            if (shape == HitboxShape.Sphere)
            {
                Gizmos.color = fillColor;
                Gizmos.DrawSphere(center, radius);
                Gizmos.color = wireColor;
                Gizmos.DrawWireSphere(center, radius);
            }
            else
            {
                Matrix4x4 previousMatrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);

                Gizmos.color = fillColor;
                Gizmos.DrawCube(Vector3.zero, boxSize);
                Gizmos.color = wireColor;
                Gizmos.DrawWireCube(Vector3.zero, boxSize);

                Gizmos.matrix = previousMatrix;
            }
        }

        #endregion
    }
}
