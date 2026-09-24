using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Woodsmen.Editor
{
    /// <summary>
    /// Editor utility for configuring optimized atmospheric lighting, URP post-processing,
    /// and sculpting the gameplay terrain (Rule 10: Explicit developer-triggered tooling).
    /// </summary>
    public static class EnvironmentLightingSetup
    {
        private const string PostProcessProfilePath = "Assets/Settings/Woodsmen_PostProcess_Profile.asset";
        private const string TerrainMaterialPath = "Assets/Settings/Woodsmen_Terrain_Lit.mat";
        private const string GrassLayerPath = "Assets/Players/Lumberjack/TerrainLayers/Grass_Layer.terrainlayer";
        private const string DirtLayerPath = "Assets/Players/Lumberjack/TerrainLayers/Dirt_Layer.terrainlayer";
        private const string RockLayerPath = "Assets/Players/Lumberjack/TerrainLayers/Rock_Layer.terrainlayer";

        [MenuItem("Tools/Woodsmen/Setup World Lighting, Post-Processing & Terrain (All-in-One)", priority = 10)]
        public static void ApplyAll()
        {
            ApplyLightingAndAtmosphere();
            ApplyPostProcessing();
            ApplyTerrainSculptingAndTexturing();

            Debug.Log("<color=#50fa7b><b>[Woodsmen]</b> Successfully applied complete environment lighting, post-processing, and terrain setup!</color>");
        }

        [MenuItem("Tools/Woodsmen/Environment/1. Apply World Lighting & Atmosphere", priority = 20)]
        public static void ApplyLightingAndAtmosphere()
        {
            // 1. Configure Directional Light (Sun)
            Light dirLight = Object.FindFirstObjectByType<Light>();
            if (dirLight == null || dirLight.type != LightType.Directional)
            {
                var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (var l in lights)
                {
                    if (l.type == LightType.Directional)
                    {
                        dirLight = l;
                        break;
                    }
                }
            }

            if (dirLight == null)
            {
                GameObject lightGo = new GameObject("Directional Light");
                dirLight = lightGo.AddComponent<Light>();
                dirLight.type = LightType.Directional;
                Undo.RegisterCreatedObjectUndo(lightGo, "Create Directional Light");
            }

            Undo.RecordObject(dirLight.gameObject, "Configure Directional Light");
            Undo.RecordObject(dirLight, "Configure Directional Light");

            // Warm golden sunlight
            dirLight.color = new Color(1.0f, 0.94f, 0.84f);
            dirLight.intensity = 1.30f;
            dirLight.shadows = LightShadows.Soft;
            dirLight.shadowResolution = LightShadowResolution.High;
            dirLight.shadowNormalBias = 0.4f;
            dirLight.shadowBias = 0.05f;

            // Dramatic top-down shadow angle
            dirLight.transform.rotation = Quaternion.Euler(48f, -38f, 0f);

            // 2. Configure RenderSettings (Ambient & Fog)
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.68f, 0.88f);      // Cool blue sky fill
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.48f, 0.42f);  // Forest canopy midtone
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.28f, 0.20f);   // Earthy moss bounce

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 35f;
            RenderSettings.fogEndDistance = 95f;
            RenderSettings.fogColor = new Color(0.55f, 0.68f, 0.82f); // Atmospheric depth haze

            EditorSceneManager.MarkSceneDirty(dirLight.gameObject.scene);
            Debug.Log("[Woodsmen] Configured warm directional sunlight, ambient contrast, and atmospheric fog.");
        }

        [MenuItem("Tools/Woodsmen/Environment/2. Apply Post-Processing & Camera Settings", priority = 21)]
        public static void ApplyPostProcessing()
        {
            // 1. Locate or create Global Volume
            Volume globalVolume = Object.FindFirstObjectByType<Volume>();
            if (globalVolume == null)
            {
                var volumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
                foreach (var v in volumes)
                {
                    if (v.isGlobal)
                    {
                        globalVolume = v;
                        break;
                    }
                }
            }

            if (globalVolume == null)
            {
                GameObject volumeGo = new GameObject("Global Volume");
                globalVolume = volumeGo.AddComponent<Volume>();
                globalVolume.isGlobal = true;
                Undo.RegisterCreatedObjectUndo(volumeGo, "Create Global Volume");
            }

            Undo.RecordObject(globalVolume, "Assign Post-Processing Profile");
            globalVolume.isGlobal = true;
            globalVolume.weight = 1.0f;

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProcessProfilePath);
            if (profile != null)
            {
                globalVolume.sharedProfile = profile;
                Debug.Log($"[Woodsmen] Assigned {PostProcessProfilePath} to Global Volume.");
            }
            else
            {
                Debug.LogWarning($"[Woodsmen] Could not find profile at {PostProcessProfilePath}. Please verify file exists.");
            }

            // 2. Configure Main Camera (FXAA + Post-Processing)
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                mainCam = Object.FindFirstObjectByType<Camera>();
            }

            if (mainCam != null)
            {
                Undo.RecordObject(mainCam.gameObject, "Configure Camera Post-Processing");
                var camData = mainCam.GetUniversalAdditionalCameraData();
                if (camData != null)
                {
                    Undo.RecordObject(camData, "Configure Camera URP Settings");
                    camData.renderPostProcessing = true;
                    camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                    Debug.Log("[Woodsmen] Enabled Post-Processing and FXAA on Main Camera.");
                }
            }

            if (globalVolume != null)
            {
                EditorSceneManager.MarkSceneDirty(globalVolume.gameObject.scene);
            }
        }

        public static Material GetOrCreateUrpTerrainMaterial()
        {
            Material terrainMat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            Shader urpTerrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");

            if (terrainMat == null)
            {
                if (urpTerrainShader == null)
                {
                    Debug.LogError("[Woodsmen] Could not find shader 'Universal Render Pipeline/Terrain/Lit'!");
                    return null;
                }

                terrainMat = new Material(urpTerrainShader);
                terrainMat.name = "Woodsmen_Terrain_Lit";
                terrainMat.enableInstancing = true;
                AssetDatabase.CreateAsset(terrainMat, TerrainMaterialPath);
                AssetDatabase.SaveAssets();
            }
            else
            {
                if (urpTerrainShader != null && (terrainMat.shader == null || terrainMat.shader != urpTerrainShader))
                {
                    terrainMat.shader = urpTerrainShader;
                    terrainMat.enableInstancing = true;
                    EditorUtility.SetDirty(terrainMat);
                    AssetDatabase.SaveAssets();
                }
            }

            return terrainMat;
        }

        [MenuItem("Tools/Woodsmen/Environment/Fix Pink Terrain (Assign URP Material)", priority = 21)]
        [MenuItem("CONTEXT/Terrain/Fix Pink Material (URP Lit)", priority = 100)]
        public static void FixTerrainMaterial()
        {
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[Woodsmen] No Terrain found in current scene!");
                return;
            }

            Material mat = GetOrCreateUrpTerrainMaterial();
            if (mat != null)
            {
                Undo.RecordObject(terrain, "Assign URP Terrain Material");
                terrain.materialTemplate = mat;
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                Debug.Log("<color=#50fa7b><b>[Woodsmen]</b> Successfully assigned URP Lit Material to Terrain! Pink shader issue resolved.</color>");
            }
        }

        [MenuItem("Tools/Woodsmen/Environment/3. Format and Sculpt Gameplay Terrain", priority = 22)]
        public static void ApplyTerrainSculptingAndTexturing()
        {
            // 1. Locate active Terrain in scene
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null)
            {
                // Try finding New Terrain.asset or creating terrain
                TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/New Terrain.asset");
                if (data == null)
                {
                    data = new TerrainData();
                    AssetDatabase.CreateAsset(data, "Assets/New Terrain.asset");
                    AssetDatabase.SaveAssets();
                }

                GameObject terrainGo = Terrain.CreateTerrainGameObject(data);
                terrain = terrainGo.GetComponent<Terrain>();
                Undo.RegisterCreatedObjectUndo(terrainGo, "Create Terrain");
            }

            TerrainData tData = terrain.terrainData;
            if (tData == null)
            {
                Debug.LogError("[Woodsmen] Terrain has no TerrainData assigned!");
                return;
            }

            Undo.RecordObject(terrain, "Configure Terrain Settings");
            Undo.RecordObject(tData, "Sculpt and Texture Terrain");

            // Assign URP Terrain Material Template
            Material terrainMat = GetOrCreateUrpTerrainMaterial();
            if (terrainMat != null)
            {
                terrain.materialTemplate = terrainMat;
            }

            // 2. Dimensions & Positioning
            float mapWidth = 130f;
            float mapLength = 130f;
            float mapHeight = 20f;
            tData.size = new Vector3(mapWidth, mapHeight, mapLength);

            // Center terrain around player spawn (~ -10, 0, -5)
            Vector3 centerTarget = new Vector3(-10f, 0f, -5f);
            terrain.transform.position = new Vector3(centerTarget.x - mapWidth * 0.5f, 0f, centerTarget.z - mapLength * 0.5f);

            // 3. Procedural Sculpting: Flat Core Clearing + Organic Outer Boundary Mounds
            int hRes = tData.heightmapResolution;
            float[,] heights = new float[hRes, hRes];

            for (int y = 0; y < hRes; y++)
            {
                for (int x = 0; x < hRes; x++)
                {
                    float nx = (float)x / (hRes - 1);
                    float ny = (float)y / (hRes - 1);

                    // Distance from center (0 = dead center, 1 = touching edge)
                    float dx = (nx - 0.5f) * 2f;
                    float dy = (ny - 0.5f) * 2f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Core gameplay zone: radius <= 0.65 is completely flat (0 height)
                    if (dist <= 0.62f)
                    {
                        heights[y, x] = 0f;
                    }
                    else
                    {
                        // Outer rim smoothly rises to 4.5m - 6.5m acting as natural boundary hills
                        float t = Mathf.Clamp01((dist - 0.62f) / 0.38f);
                        float smoothT = Mathf.SmoothStep(0f, 1f, t);
                        float perlin = Mathf.PerlinNoise(nx * 5f, ny * 5f) * 0.08f;
                        float maxHeightFrac = (5.5f / mapHeight); // ~5.5 meters height

                        heights[y, x] = smoothT * (maxHeightFrac + perlin);
                    }
                }
            }

            tData.SetHeights(0, 0, heights);

            // 4. Assign Terrain Layers (Grass, Dirt, Rock)
            TerrainLayer grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>(GrassLayerPath);
            TerrainLayer dirt = AssetDatabase.LoadAssetAtPath<TerrainLayer>(DirtLayerPath);
            TerrainLayer rock = AssetDatabase.LoadAssetAtPath<TerrainLayer>(RockLayerPath);

            if (grass != null && dirt != null && rock != null)
            {
                tData.terrainLayers = new TerrainLayer[] { grass, dirt, rock };

                // 5. Paint Alphamap (Splatmap)
                int aRes = tData.alphamapResolution;
                float[,,] splat = new float[aRes, aRes, 3];

                for (int y = 0; y < aRes; y++)
                {
                    for (int x = 0; x < aRes; x++)
                    {
                        float nx = (float)x / (aRes - 1);
                        float ny = (float)y / (aRes - 1);

                        float dx = (nx - 0.5f) * 2f;
                        float dy = (ny - 0.5f) * 2f;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);

                        float grassWeight = 1.0f;
                        float dirtWeight = 0.0f;
                        float rockWeight = 0.0f;

                        if (dist > 0.70f)
                        {
                            // Outer hills blend to rock
                            float t = Mathf.Clamp01((dist - 0.70f) / 0.30f);
                            rockWeight = Mathf.SmoothStep(0f, 1f, t);
                            grassWeight = 1.0f - rockWeight;
                        }
                        else if (dist < 0.25f)
                        {
                            // Subtle earthy clearing patches near center
                            float dirtNoise = Mathf.PerlinNoise(nx * 8f, ny * 8f);
                            if (dirtNoise > 0.65f)
                            {
                                dirtWeight = (dirtNoise - 0.65f) * 1.5f;
                                grassWeight = Mathf.Max(0f, 1f - dirtWeight);
                            }
                        }

                        // Normalize
                        float sum = grassWeight + dirtWeight + rockWeight;
                        if (sum > 0f)
                        {
                            splat[y, x, 0] = grassWeight / sum;
                            splat[y, x, 1] = dirtWeight / sum;
                            splat[y, x, 2] = rockWeight / sum;
                        }
                        else
                        {
                            splat[y, x, 0] = 1f;
                        }
                    }
                }

                tData.SetAlphamaps(0, 0, splat);
                Debug.Log("[Woodsmen] Applied Grass, Dirt, and Rock TerrainLayers with natural border distribution.");
            }
            else
            {
                Debug.LogWarning("[Woodsmen] One or more TerrainLayer assets could not be found. Check TerrainLayers directory.");
            }

            // 6. Terrain Quality Settings (Smooth and High Performance)
            terrain.heightmapPixelError = 5f;
            terrain.basemapDistance = 90f;
            terrain.shadowCastingMode = ShadowCastingMode.TwoSided;
            terrain.drawTreesAndFoliage = true;

            // Ensure TerrainCollider is present
            if (!terrain.TryGetComponent(out TerrainCollider collider))
            {
                collider = terrain.gameObject.AddComponent<TerrainCollider>();
            }
            collider.terrainData = tData;

            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"<color=#50fa7b><b>[Woodsmen]</b> Terrain successfully formatted at {terrain.transform.position} with flat central clearing and border ridges!</color>");
        }

        [MenuItem("Tools/Woodsmen/Environment/4. Spawn Destructible Trees on Active Terrain", priority = 23)]
        public static void SpawnTreesOnActiveTerrain()
        {
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[Woodsmen] No active Terrain found in scene!");
                return;
            }

            if (!terrain.TryGetComponent(out Woodsmen.Environment.TerrainTreeSpawner spawner))
            {
                spawner = Undo.AddComponent<Woodsmen.Environment.TerrainTreeSpawner>(terrain.gameObject);
            }

            // Ensure tree prefabs are populated
            if (spawner.TreePrefabs == null || spawner.TreePrefabs.Length == 0)
            {
                string[] paths = new string[]
                {
                    "Assets/SimpleNaturePack/Prefabs/Tree_01.prefab",
                    "Assets/SimpleNaturePack/Prefabs/Tree_02.prefab",
                    "Assets/SimpleNaturePack/Prefabs/Tree_03.prefab",
                    "Assets/SimpleNaturePack/Prefabs/Tree_04.prefab",
                    "Assets/SimpleNaturePack/Prefabs/Tree_05.prefab"
                };

                var list = new System.Collections.Generic.List<GameObject>();
                foreach (var path in paths)
                {
                    GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (p != null) list.Add(p);
                }
                spawner.TreePrefabs = list.ToArray();
            }

            spawner.GenerateTrees();
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        }

        [MenuItem("Tools/Woodsmen/Environment/Clear Spawned Trees on Active Terrain", priority = 24)]
        public static void ClearTreesOnActiveTerrain()
        {
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[Woodsmen] No active Terrain found in scene!");
                return;
            }

            if (terrain.TryGetComponent(out Woodsmen.Environment.TerrainTreeSpawner spawner))
            {
                spawner.ClearTrees();
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            }
        }

        [MenuItem("Tools/Woodsmen/Setup Character Health on Prefabs & Scene Players", priority = 30)]
        public static void SetupCharacterHealth()
        {
            // 1. Prefabs
            string[] prefabPaths = new string[]
            {
                "Assets/Players/Lumberjack/Lumberjack.prefab",
                "Assets/Players/Warrior/Warrior.prefab"
            };

            foreach (var path in prefabPaths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root != null)
                {
                    if (!root.TryGetComponent(out Woodsmen.Combat.CharacterHealth _))
                    {
                        root.AddComponent<Woodsmen.Combat.CharacterHealth>();
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    PrefabUtility.UnloadPrefabContents(root);
                    Debug.Log($"<color=#50fa7b><b>[Woodsmen]</b> CharacterHealth attached and saved to '{path}'.</color>");
                }
            }

            // 2. Scene Instances
            var locomotionControllers = Object.FindObjectsByType<Woodsmen.Players.LocomotionController>(FindObjectsSortMode.None);
            foreach (var lc in locomotionControllers)
            {
                if (!lc.TryGetComponent(out Woodsmen.Combat.CharacterHealth _))
                {
                    Undo.AddComponent<Woodsmen.Combat.CharacterHealth>(lc.gameObject);
                    EditorSceneManager.MarkSceneDirty(lc.gameObject.scene);
                    Debug.Log($"<color=#50fa7b><b>[Woodsmen]</b> CharacterHealth attached to scene player '{lc.gameObject.name}'.</color>");
                }
            }

            AssetDatabase.SaveAssets();
        }
    }
}
