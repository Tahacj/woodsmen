using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Woodsmen.Players;

namespace Woodsmen.Editor
{
    /// <summary>
    /// Editor utility to automatically construct and configure the 8-directional 2D BlendTree
    /// for humanoid locomotion (Lumberjack and Warrior), configure loop settings on imported FBX clips,
    /// and link the parameters (MoveX, MoveZ, Speed).
    /// </summary>
    public static class LocomotionBlendTreeBuilder
    {
        private const string LocomotionAnimationsFolder = "Assets/Players/Locomotion Animations";
        private const string UniversalControllerPath = "Assets/Players/Locomotion Animations/HumanoidLocomotion.controller";
        private const string LumberjackControllerPath = "Assets/Players/Lumberjack/Animations/Lumberjack Animator.controller";
        private const string UpperBodyMaskPath = "Assets/Players/Universal Scripts/UpperBody.mask";
        private const string HorizontalCutFbx = "Assets/Players/Lumberjack/Animations/Actions Animations/Remy@Standing Melee Attack Horizontal.fbx";
        private const string DownwardCutFbx = "Assets/Players/Lumberjack/Animations/Actions Animations/Standing Melee Attack Downward.fbx";

        [MenuItem("Tools/Woodsmen/Setup Locomotion BlendTree")]
        public static void BuildLocomotionBlendTree()
        {
            EnsureAnimationLooping();
            BuildController(UniversalControllerPath);

            // Also synchronize with Lumberjack's controller
            if (File.Exists(LumberjackControllerPath))
            {
                BuildController(LumberjackControllerPath);
                SetupLumberjackCuttingAction();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Woodsmen] Successfully built and configured 8-directional Locomotion Blend Tree!");
        }

        private static void EnsureAnimationLooping()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { LocomotionAnimationsFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null)
                {
                    ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
                    bool modified = false;

                    foreach (ModelImporterClipAnimation clip in clips)
                    {
                        if (!clip.loopTime)
                        {
                            clip.loopTime = true;
                            clip.loopPose = true;
                            modified = true;
                        }
                    }

                    if (modified)
                    {
                        importer.clipAnimations = clips;
                        importer.SaveAndReimport();
                    }
                }
            }
        }

        private static void BuildController(string controllerPath)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            }

            // Ensure parameters
            EnsureParameter(controller, "MoveX", AnimatorControllerParameterType.Float);
            EnsureParameter(controller, "MoveZ", AnimatorControllerParameterType.Float);
            EnsureParameter(controller, "Speed", AnimatorControllerParameterType.Float);

            // Locate Locomotion state in base layer
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState locomotionState = null;

            foreach (ChildAnimatorState child in stateMachine.states)
            {
                if (child.state.name == "Locomotion")
                {
                    locomotionState = child.state;
                    break;
                }
            }

            if (locomotionState == null)
            {
                locomotionState = stateMachine.AddState("Locomotion", new Vector3(300, 110, 0));
                stateMachine.defaultState = locomotionState;
            }

            // Create/Assign 2D Blend Tree
            BlendTree blendTree = locomotionState.motion as BlendTree;
            if (blendTree == null)
            {
                blendTree = new BlendTree
                {
                    name = "8-Directional Blend Tree",
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(blendTree, controller);
                locomotionState.motion = blendTree;
            }

            blendTree.blendType = BlendTreeType.FreeformDirectional2D;
            blendTree.blendParameter = "MoveX";
            blendTree.blendParameterY = "MoveZ";

            // Clear old children
            while (blendTree.children.Length > 0)
            {
                blendTree.RemoveChild(0);
            }

            // Map 8 directions + Idle
            AddMotionToTree(blendTree, $"{LocomotionAnimationsFolder}/idle.fbx", new Vector2(0f, 0f));
            AddMotionToTree(blendTree, $"{LocomotionAnimationsFolder}/Y Bot@Fast Run.fbx", new Vector2(0f, 1f));
            AddMotionToTree(blendTree, $"{LocomotionAnimationsFolder}/run_back.fbx", new Vector2(0f, -1f));
            AddMotionToTree(blendTree, $"{LocomotionAnimationsFolder}/run_left.fbx", new Vector2(-1f, 0f));
            AddMotionToTree(blendTree, $"{LocomotionAnimationsFolder}/run_right.fbx", new Vector2(1f, 0f));
            AddMotionToTree(blendTree, $"{LocomotionAnimationsFolder}/run_frontL45.fbx", new Vector2(-0.707f, 0.707f));
            AddMotionToTree(blendTree, $"{LocomotionAnimationsFolder}/run_frontR45.fbx", new Vector2(0.707f, 0.707f));
            AddMotionToTree(blendTree, $"{LocomotionAnimationsFolder}/run_backL45.fbx", new Vector2(-0.707f, -0.707f));
            AddMotionToTree(blendTree, $"{LocomotionAnimationsFolder}/run_backR45.fbx", new Vector2(0.707f, -0.707f));

            EditorUtility.SetDirty(controller);
        }

        [MenuItem("Tools/Woodsmen/Setup Player Prefabs (Lumberjack & Warrior)")]
        public static void SetupPlayerPrefabs()
        {
            BuildLocomotionBlendTree();

            string[] prefabPaths = new[]
            {
                "Assets/Players/Lumberjack/Lumberjack.prefab",
                "Assets/Players/Warrior/Warrior.prefab"
            };

            UnityEngine.InputSystem.InputActionAsset inputAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>("Assets/Players/Input/PlayerInputActions.inputactions");

            foreach (string path in prefabPaths)
            {
                if (!File.Exists(path)) continue;

                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                if (prefabRoot == null) continue;

                // 1. Ensure NetworkIdentity
                if (!prefabRoot.TryGetComponent(out Mirror.NetworkIdentity _))
                {
                    prefabRoot.AddComponent<Mirror.NetworkIdentity>();
                }

                // 2. Ensure NetworkTransformReliable
                if (!prefabRoot.TryGetComponent(out Mirror.NetworkTransformReliable _))
                {
                    prefabRoot.AddComponent<Mirror.NetworkTransformReliable>();
                }

                // 3. Ensure CharacterController
                if (!prefabRoot.TryGetComponent(out CharacterController cc))
                {
                    cc = prefabRoot.AddComponent<CharacterController>();
                }
                cc.height = 1.8f;
                cc.radius = 0.35f;
                cc.center = new Vector3(0f, 0.9f, 0f);

                // 4. Ensure PlayerInputReader and assign input actions asset
                if (!prefabRoot.TryGetComponent(out PlayerInputReader inputReader))
                {
                    inputReader = prefabRoot.AddComponent<PlayerInputReader>();
                }
                if (inputAsset != null)
                {
                    SerializedObject serializedReader = new SerializedObject(inputReader);
                    SerializedProperty prop = serializedReader.FindProperty("customInputActions");
                    if (prop != null)
                    {
                        prop.objectReferenceValue = inputAsset;
                        serializedReader.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                // 5. Ensure LocomotionController
                if (!prefabRoot.TryGetComponent(out LocomotionController _))
                {
                    prefabRoot.AddComponent<LocomotionController>();
                }

                // 5b. Ensure LumberjackChopping on Lumberjack
                if (path.Contains("Lumberjack") && !prefabRoot.TryGetComponent(out LumberjackChopping _))
                {
                    prefabRoot.AddComponent<LumberjackChopping>();
                }

                // 6. Ensure correct Animator Controller & strictly enforce No Root Motion
                string controllerPath = path.Contains("Lumberjack") ? LumberjackControllerPath : UniversalControllerPath;
                RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);

                if (prefabRoot.TryGetComponent(out Animator animator))
                {
                    if (controller != null)
                    {
                        animator.runtimeAnimatorController = controller;
                    }
                    animator.applyRootMotion = false;
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                PrefabUtility.UnloadPrefabContents(prefabRoot);
                Debug.Log($"[Woodsmen] Successfully configured player prefab at {path}");
            }

            // Also configure Lumberjack Cutting Layer
            SetupLumberjackCuttingAction();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Woodsmen/Setup Destructible Trees")]
        public static void SetupEnvironmentTrees()
        {
            string[] treePrefabPaths = new[]
            {
                "Assets/SimpleNaturePack/Prefabs/Tree_01.prefab",
                "Assets/SimpleNaturePack/Prefabs/Tree_02.prefab",
                "Assets/SimpleNaturePack/Prefabs/Tree_03.prefab",
                "Assets/SimpleNaturePack/Prefabs/Tree_04.prefab",
                "Assets/SimpleNaturePack/Prefabs/Tree_05.prefab"
            };

            GameObject stumpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SimpleNaturePack/Prefabs/Stump_01.prefab");

            foreach (string path in treePrefabPaths)
            {
                if (!File.Exists(path)) continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;

                // 1. Ensure Collider
                if (!root.TryGetComponent(out Collider _))
                {
                    CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
                    capsule.center = new Vector3(0f, 2.0f, 0f);
                    capsule.radius = 0.4f;
                    capsule.height = 4.0f;
                }

                // 2. Ensure NavMeshObstacle with Carve = true
                if (!root.TryGetComponent(out UnityEngine.AI.NavMeshObstacle navObstacle))
                {
                    navObstacle = root.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                }
                navObstacle.carving = true;
                navObstacle.carveOnlyStationary = false;

                // 3. Ensure DestructibleTree
                if (!root.TryGetComponent(out Woodsmen.Environment.DestructibleTree treeComp))
                {
                    treeComp = root.AddComponent<Woodsmen.Environment.DestructibleTree>();
                }

                if (stumpPrefab != null)
                {
                    SerializedObject serializedTree = new SerializedObject(treeComp);
                    SerializedProperty stumpProp = serializedTree.FindProperty("stumpPrefab");
                    if (stumpProp != null)
                    {
                        stumpProp.objectReferenceValue = stumpPrefab;
                        serializedTree.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                Debug.Log($"[Woodsmen] Successfully configured DestructibleTree prefab: {path}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Woodsmen/Setup Gameplay Scene Environment & Player")]
        public static void EnsureSceneSetup()
        {
            // Ensure scene Lumberjack has LumberjackChopping
            var lumberjackInScene = Object.FindObjectsByType<LocomotionController>(FindObjectsSortMode.None);
            foreach (var lc in lumberjackInScene)
            {
                if (lc.gameObject.name.Contains("Lumberjack") && !lc.gameObject.TryGetComponent(out LumberjackChopping _))
                {
                    lc.gameObject.AddComponent<LumberjackChopping>();
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lc.gameObject.scene);
                }
            }

            // Ensure there is at least one DestructibleTree in the scene
            var existingTrees = Object.FindObjectsByType<Woodsmen.Environment.DestructibleTree>(FindObjectsSortMode.None);
            if (existingTrees == null || existingTrees.Length == 0)
            {
                GameObject treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SimpleNaturePack/Prefabs/Tree_01.prefab");
                if (treePrefab != null)
                {
                    var player = Object.FindFirstObjectByType<LocomotionController>();
                    Vector3 spawnPos = player != null ? player.transform.position + player.transform.forward * 2.2f : new Vector3(-10f, 0.28f, -2.2f);
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(treePrefab);
                    instance.transform.position = spawnPos;
                    Undo.RegisterCreatedObjectUndo(instance, "Spawn Test Destructible Tree");
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(instance.scene);
                    Debug.Log($"[Woodsmen] Placed test DestructibleTree in scene at {spawnPos}");
                }
            }

            // Ensure VFXManager exists in the scene
            var vfxManager = Object.FindFirstObjectByType<Woodsmen.Feedback.VFXManager>();
            if (vfxManager == null)
            {
                GameObject vfxGO = new GameObject("VFXManager");
                vfxManager = vfxGO.AddComponent<Woodsmen.Feedback.VFXManager>();
                GameObject vfxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Players/Lumberjack/FX_Impact_Wood_Ztest 8.prefab");
                if (vfxPrefab != null)
                {
                    SerializedObject so = new SerializedObject(vfxManager);
                    so.FindProperty("woodImpactPrefab").objectReferenceValue = vfxPrefab;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                Undo.RegisterCreatedObjectUndo(vfxGO, "Create VFXManager");
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(vfxGO.scene);
                Debug.Log("[Woodsmen] Successfully created VFXManager in scene with wood impact prefab.");
            }
        }

        [MenuItem("Tools/Woodsmen/Setup Lumberjack Cutting Action")]
        public static void SetupLumberjackCuttingAction()
        {
            AvatarMask upperBodyMask = BuildUpperBodyMask();

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(LumberjackControllerPath);
            if (controller == null)
            {
                Debug.LogWarning($"[Woodsmen] No controller found at {LumberjackControllerPath} to configure cutting layer.");
                return;
            }

            // Ensure parameters for speed multiplier and action indexing
            EnsureParameter(controller, "IsCutting", AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, "ChopSpeed", AnimatorControllerParameterType.Float);
            EnsureParameter(controller, "ChopIndex", AnimatorControllerParameterType.Int);

            // Default ChopSpeed to 1.0f if uninitialized
            for (int i = 0; i < controller.parameters.Length; i++)
            {
                if (controller.parameters[i].name == "ChopSpeed" && controller.parameters[i].defaultFloat <= 0f)
                {
                    controller.parameters[i].defaultFloat = 1.0f;
                }
            }

            // Ensure Cutting Layer exists
            int cuttingLayerIndex = -1;
            for (int i = 0; i < controller.layers.Length; i++)
            {
                if (controller.layers[i].name == "Cutting Layer")
                {
                    cuttingLayerIndex = i;
                    break;
                }
            }

            if (cuttingLayerIndex == -1)
            {
                controller.AddLayer("Cutting Layer");
                cuttingLayerIndex = controller.layers.Length - 1;
            }

            // Configure layer with UpperBody mask & Override blending
            AnimatorControllerLayer[] layers = controller.layers;
            layers[cuttingLayerIndex].defaultWeight = 1.0f;
            layers[cuttingLayerIndex].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[cuttingLayerIndex].avatarMask = upperBodyMask;

            AnimatorStateMachine stateMachine = layers[cuttingLayerIndex].stateMachine;
            if (stateMachine == null)
            {
                stateMachine = new AnimatorStateMachine
                {
                    name = "Cutting Layer",
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(stateMachine, controller);
                layers[cuttingLayerIndex].stateMachine = stateMachine;
            }

            controller.layers = layers;

            // Load cutting animation clips
            AnimationClip horizontalClip = GetFirstClipFromFbx(HorizontalCutFbx);
            AnimationClip downwardClip = GetFirstClipFromFbx(DownwardCutFbx);

            // Setup States: Empty (Default), Cut_Horizontal, Cut_Downward
            AnimatorState emptyState = FindOrCreateState(stateMachine, "Empty", null, new Vector3(250, 0, 0));
            AnimatorState cutHorizontalState = FindOrCreateState(stateMachine, "Cut_Horizontal", horizontalClip, new Vector3(250, 100, 0));
            AnimatorState cutDownwardState = FindOrCreateState(stateMachine, "Cut_Downward", downwardClip, new Vector3(250, 200, 0));

            // Enable dynamic animation speed multiplier driven by ChopSpeed parameter
            cutHorizontalState.speedParameterActive = true;
            cutHorizontalState.speedParameter = "ChopSpeed";

            cutDownwardState.speedParameterActive = true;
            cutDownwardState.speedParameter = "ChopSpeed";

            stateMachine.defaultState = emptyState;

            // Clear old transitions to prevent stacking
            ClearTransitions(emptyState);
            ClearTransitions(cutHorizontalState);
            ClearTransitions(cutDownwardState);

            // 1. Empty -> Cut_Horizontal (when IsCutting == true && ChopIndex == 0)
            var toCut1 = emptyState.AddTransition(cutHorizontalState);
            toCut1.hasExitTime = false;
            toCut1.duration = 0.08f;
            toCut1.AddCondition(AnimatorConditionMode.If, 0, "IsCutting");
            toCut1.AddCondition(AnimatorConditionMode.Equals, 0, "ChopIndex");

            // 2. Empty -> Cut_Downward (when IsCutting == true && ChopIndex == 1)
            var toCut2 = emptyState.AddTransition(cutDownwardState);
            toCut2.hasExitTime = false;
            toCut2.duration = 0.08f;
            toCut2.AddCondition(AnimatorConditionMode.If, 0, "IsCutting");
            toCut2.AddCondition(AnimatorConditionMode.Equals, 1, "ChopIndex");

            // 3. Cut_Horizontal -> Empty (when IsCutting == false, returns to rest during cooldown)
            var cut1ToEmpty = cutHorizontalState.AddTransition(emptyState);
            cut1ToEmpty.hasExitTime = false;
            cut1ToEmpty.duration = 0.15f;
            cut1ToEmpty.AddCondition(AnimatorConditionMode.IfNot, 0, "IsCutting");

            // 4. Cut_Downward -> Empty (when IsCutting == false, returns to rest during cooldown)
            var cut2ToEmpty = cutDownwardState.AddTransition(emptyState);
            cut2ToEmpty.hasExitTime = false;
            cut2ToEmpty.duration = 0.15f;
            cut2ToEmpty.AddCondition(AnimatorConditionMode.IfNot, 0, "IsCutting");

            EditorUtility.SetDirty(controller);
            Debug.Log("[Woodsmen] Successfully built Cutting Layer with Speed Multiplier & Indexing on Lumberjack Animator!");
        }

        public static AvatarMask BuildUpperBodyMask()
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            }

            // Disable lower body so locomotion runs uninterrupted
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);

            // Enable upper body for swinging
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);

            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static AnimationClip GetFirstClipFromFbx(string fbxPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (Object asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    return clip;
                }
            }
            Debug.LogWarning($"[Woodsmen] No AnimationClip found in {fbxPath}");
            return null;
        }

        private static AnimatorState FindOrCreateState(AnimatorStateMachine sm, string name, Motion motion, Vector3 position)
        {
            foreach (ChildAnimatorState child in sm.states)
            {
                if (child.state != null && child.state.name == name)
                {
                    child.state.motion = motion;
                    return child.state;
                }
            }
            AnimatorState state = sm.AddState(name, position);
            state.motion = motion;
            return state;
        }

        private static void ClearTransitions(AnimatorState state)
        {
            while (state.transitions.Length > 0)
            {
                state.RemoveTransition(state.transitions[0]);
            }
        }

        private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            foreach (var param in controller.parameters)
            {
                if (param.name == name) return;
            }
            controller.AddParameter(name, type);
        }

        private static void AddMotionToTree(BlendTree tree, string fbxPath, Vector2 pos)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            AnimationClip clip = null;

            foreach (Object asset in assets)
            {
                if (asset is AnimationClip c && !c.name.StartsWith("__preview__"))
                {
                    clip = c;
                    break;
                }
            }

            if (clip != null)
            {
                tree.AddChild(clip, pos);
            }
            else
            {
                Debug.LogWarning($"[Woodsmen] No AnimationClip found in {fbxPath}");
            }
        }
    }
}
