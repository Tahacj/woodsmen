using Mirror;
using UnityEngine;
using Woodsmen.Inventory;

namespace Woodsmen.Networking
{
    /// <summary>
    /// Visual and configurable player spawn point in the scene.
    /// Works with Mirror's NetworkStartPosition and WoodsmenNetworkManager.
    /// Features:
    /// - Class-specific spawn routing (Lumberjack, Warrior, or Both).
    /// - Rich 3D editor Gizmo: ground disc, character capsule outline, and forward orientation arrow.
    /// - 1-click snap-to-ground utility in context menu.
    /// </summary>
    [RequireComponent(typeof(NetworkStartPosition))]
    [SelectionBase]
    public class PlayerSpawnPoint : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [Tooltip("Which character class should prioritize spawning at this location. 'Both' acts as a universal spawn point.")]
        [SerializeField] private CharacterClass preferredClass = CharacterClass.Both;

        public CharacterClass PreferredClass
        {
            get => preferredClass;
            set => preferredClass = value;
        }

        [Header("Editor Gizmo Settings")]
        [SerializeField] private bool showGizmo = true;
        [SerializeField] private float gizmoRadius = 0.5f;
        [SerializeField] private float characterHeight = 1.8f;
        [SerializeField] private float arrowLength = 1.4f;

        /// <summary>
        /// Snaps this spawn point directly to the terrain or collider beneath it.
        /// </summary>
        [ContextMenu("Snap to Ground")]
        public void SnapToGround()
        {
            Vector3 start = transform.position + Vector3.up * 10f;
            if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, 50f))
            {
                #if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(transform, "Snap Spawn Point to Ground");
                #endif
                transform.position = hit.point;
            }
        }

        private void OnDrawGizmos()
        {
            if (!showGizmo) return;

            Color themeColor;
            switch (preferredClass)
            {
                case CharacterClass.Lumberjack:
                    themeColor = new Color(0.2f, 0.85f, 0.35f, 0.9f); // Green
                    break;
                case CharacterClass.Warrior:
                    themeColor = new Color(0.95f, 0.40f, 0.15f, 0.9f); // Orange-red
                    break;
                default:
                    themeColor = new Color(0.25f, 0.75f, 1f, 0.9f); // Cyan / Blue
                    break;
            }

            Vector3 pos = transform.position;
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;

            // 1. Ground Ring
            Gizmos.color = themeColor;
            DrawWireCircle(pos + Vector3.up * 0.02f, gizmoRadius, 24);

            // 2. Character body representation (capsule/cylinder)
            Vector3 center = pos + Vector3.up * (characterHeight * 0.5f);
            Gizmos.color = new Color(themeColor.r, themeColor.g, themeColor.b, 0.35f);
            Gizmos.DrawWireCube(center, new Vector3(gizmoRadius * 1.5f, characterHeight, gizmoRadius * 1.5f));

            // Head sphere
            Gizmos.color = themeColor;
            Gizmos.DrawWireSphere(pos + Vector3.up * (characterHeight - 0.2f), 0.22f);

            // 3. Forward orientation arrow
            Vector3 arrowStart = pos + Vector3.up * 0.2f;
            Vector3 arrowTip = arrowStart + forward * arrowLength;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(arrowStart, arrowTip);

            // Arrow head
            Vector3 leftWing = arrowTip - forward * 0.35f + right * 0.20f;
            Vector3 rightWing = arrowTip - forward * 0.35f - right * 0.20f;
            Gizmos.DrawLine(arrowTip, leftWing);
            Gizmos.DrawLine(arrowTip, rightWing);
            Gizmos.DrawLine(leftWing, rightWing);

            #if UNITY_EDITOR
            // Label above spawn point
            string label = preferredClass == CharacterClass.Both
                ? $"Spawn Point\n(Any Class)"
                : $"Spawn Point\n({preferredClass})";
            UnityEditor.Handles.Label(pos + Vector3.up * (characterHeight + 0.3f), label);
            #endif
        }

        private void DrawWireCircle(Vector3 center, float radius, int segments)
        {
            float step = 360f / segments;
            Vector3 prev = center + new Vector3(radius, 0f, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float rad = i * step * Mathf.Deg2Rad;
                Vector3 next = center + new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
