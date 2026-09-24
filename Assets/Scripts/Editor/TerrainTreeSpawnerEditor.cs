using UnityEditor;
using UnityEngine;
using Woodsmen.Environment;

namespace Woodsmen.Editor
{
    [CustomEditor(typeof(TerrainTreeSpawner))]
    public class TerrainTreeSpawnerEditor : UnityEditor.Editor
    {
        private static readonly string[] DefaultPrefabPaths = new string[]
        {
            "Assets/SimpleNaturePack/Prefabs/Tree_01.prefab",
            "Assets/SimpleNaturePack/Prefabs/Tree_02.prefab",
            "Assets/SimpleNaturePack/Prefabs/Tree_03.prefab",
            "Assets/SimpleNaturePack/Prefabs/Tree_04.prefab",
            "Assets/SimpleNaturePack/Prefabs/Tree_05.prefab"
        };

        public override void OnInspectorGUI()
        {
            TerrainTreeSpawner spawner = (TerrainTreeSpawner)target;

            serializedObject.Update();

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "🌲 Terrain Tree Spawner\n" +
                "Configures and spawns persistent destructible trees across the terrain.\n" +
                "• GPU Instanced rendering batches hundreds of trees into 1 draw call.\n" +
                "• Automatic Frustum Culling skips offscreen trees without destroying them (retains chopped state & health).\n" +
                "• NavMeshObstacle carving opens paths dynamically when trees are felled.",
                MessageType.Info);

            EditorGUILayout.Space(6);

            // Quick auto-populate button if palette is empty
            SerializedProperty prefabsProp = serializedObject.FindProperty("treePrefabs");
            if (prefabsProp.arraySize == 0)
            {
                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                if (GUILayout.Button("⚡ Auto-Assign 5 Destructible Tree Prefabs (Tree_01 - 05)", GUILayout.Height(30)))
                {
                    AutoAssignPrefabs(spawner);
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.Space(6);
            }

            DrawDefaultInspector();

            EditorGUILayout.Space(10);

            // Status bar
            int currentSpawned = spawner.SpawnedTreeCount;
            int targetCount = spawner.TreeCount;

            GUIStyle statusStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };

            EditorGUILayout.LabelField($"Spawned Trees: {currentSpawned} / {targetCount}", statusStyle);

            EditorGUILayout.Space(6);

            // Action Buttons
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
            if (GUILayout.Button("🌲 Generate Trees (Bake)", GUILayout.Height(36)))
            {
                spawner.GenerateTrees();
                EditorUtility.SetDirty(spawner);
            }

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("🧹 Clear Trees", GUILayout.Height(36)))
            {
                if (EditorUtility.DisplayDialog("Clear Spawned Trees", "Are you sure you want to remove all spawned trees from this terrain?", "Clear", "Cancel"))
                {
                    spawner.ClearTrees();
                    EditorUtility.SetDirty(spawner);
                }
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            serializedObject.ApplyModifiedProperties();
        }

        private static void AutoAssignPrefabs(TerrainTreeSpawner spawner)
        {
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var path in DefaultPrefabPaths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    list.Add(prefab);
                }
            }

            if (list.Count > 0)
            {
                Undo.RecordObject(spawner, "Auto-Assign Tree Prefabs");
                spawner.TreePrefabs = list.ToArray();
                EditorUtility.SetDirty(spawner);
                Debug.Log($"<color=#50fa7b><b>[Woodsmen]</b> Loaded {list.Count} NaturePack tree prefabs into palette.</color>");
            }
        }
    }
}
