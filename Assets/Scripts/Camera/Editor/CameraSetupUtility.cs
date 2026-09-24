using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Woodsmen.CameraSystem;

namespace Woodsmen.Editor
{
    /// <summary>
    /// One-click setup utility to configure Cinemachine 3, CameraManager brain,
    /// Impulse shake listener/source, and scene test ground.
    /// </summary>
    public static class CameraSetupUtility
    {
        [MenuItem("Tools/Woodsmen/Setup Scene Camera (Cinemachine)")]
        public static void SetupSceneCamera()
        {
            // 1. Configure Main Camera
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                mainCam = camObj.AddComponent<Camera>();
                camObj.tag = "MainCamera";
                camObj.AddComponent<AudioListener>();
            }

            // Ensure CinemachineBrain
            if (!mainCam.TryGetComponent(out CinemachineBrain _))
            {
                mainCam.gameObject.AddComponent<CinemachineBrain>();
                Debug.Log("[Woodsmen] Added CinemachineBrain to Main Camera.");
            }

            // Ensure CinemachineImpulseListener on Main Camera
            if (!mainCam.TryGetComponent(out CinemachineImpulseListener _))
            {
                mainCam.gameObject.AddComponent<CinemachineImpulseListener>();
                Debug.Log("[Woodsmen] Added CinemachineImpulseListener to Main Camera.");
            }

            // 2. Find or Create CameraManager & CinemachineCamera
            CameraManager cameraManager = Object.FindFirstObjectByType<CameraManager>();
            GameObject managerObj;

            if (cameraManager == null)
            {
                managerObj = new GameObject("Camera Manager");
                cameraManager = managerObj.AddComponent<CameraManager>();
                Undo.RegisterCreatedObjectUndo(managerObj, "Create Camera Manager");
            }
            else
            {
                managerObj = cameraManager.gameObject;
            }

            // Ensure CinemachineCamera on CameraManager object
            if (!managerObj.TryGetComponent(out CinemachineCamera vcam))
            {
                vcam = managerObj.AddComponent<CinemachineCamera>();
            }

            // Ensure CinemachineFollow on CinemachineCamera
            if (!managerObj.TryGetComponent(out CinemachineFollow follow))
            {
                follow = managerObj.AddComponent<CinemachineFollow>();
            }

            // Configure top-down default angles and offsets
            follow.FollowOffset = new Vector3(0f, 14f, -10f);
            managerObj.transform.rotation = Quaternion.Euler(55f, 0f, 0f);

            // Ensure CinemachineImpulseSource on CameraManager
            if (!managerObj.TryGetComponent(out CinemachineImpulseSource impulseSource))
            {
                impulseSource = managerObj.AddComponent<CinemachineImpulseSource>();
            }

            // Link serialized references to CameraManager
            SerializedObject serializedManager = new SerializedObject(cameraManager);
            serializedManager.FindProperty("cinemachineCam").objectReferenceValue = vcam;
            serializedManager.FindProperty("impulseSource").objectReferenceValue = impulseSource;
            serializedManager.FindProperty("cinemachineFollow").objectReferenceValue = follow;
            serializedManager.ApplyModifiedProperties();

            // 3. Ensure a test ground plane exists with a Collider so player doesn't fall into void
            EnsureTestGround();

            EditorSceneManager.MarkSceneDirty(mainCam.scene);
            Debug.Log("[Woodsmen] Successfully configured Scene Camera with Cinemachine 3, Impulse Listener, and CameraManager!");
        }

        private static void EnsureTestGround()
        {
            Collider existingCollider = Object.FindFirstObjectByType<Collider>();
            if (existingCollider == null)
            {
                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.position = Vector3.zero;
                ground.transform.localScale = new Vector3(5f, 1f, 5f);
                Undo.RegisterCreatedObjectUndo(ground, "Create Ground Plane");
                Debug.Log("[Woodsmen] Created default Ground Plane with Collider at (0,0,0).");
            }
        }
    }
}
