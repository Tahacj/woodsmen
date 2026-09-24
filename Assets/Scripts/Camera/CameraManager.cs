using PrimeTween;
using Unity.Cinemachine;
using UnityEngine;
using Woodsmen.Utilities;

namespace Woodsmen.CameraSystem
{
    /// <summary>
    /// Centralized Camera Manager and Brain for the top-down perspective.
    /// Manages CinemachineCamera framing, smooth target tracking for the local player,
    /// and provides multiplayer-isolated camera shakes via Cinemachine Impulse (No Coroutines).
    /// </summary>
    public class CameraManager : MonoBehaviour
    {
        public static CameraManager Instance { get; private set; }

        [Header("Cinemachine References")]
        [SerializeField] private CinemachineCamera cinemachineCam;
        [SerializeField] private CinemachineImpulseSource impulseSource;
        [SerializeField] private CinemachineFollow cinemachineFollow;

        [Header("Top-Down Framing Controls")]
        [Tooltip("Downward tilt angle in degrees (55 is standard top-down ARPG, 90 is pure overhead).")]
        [Range(20f, 90f)]
        [SerializeField] private float tiltAngle = 55f;

        [Tooltip("Offset relative to player: X = sideways, Y = height/distance, Z = backward pull.")]
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 14f, -10f);

        [Tooltip("Field of View in degrees. Lower values flatten perspective; higher values show more area.")]
        [Range(20f, 90f)]
        [SerializeField] private float fieldOfView = 50f;

        [Tooltip("Follow lag/smoothness. 0 = rigid lock, 0.3 to 0.5 = smooth cinematic easing.")]
        [SerializeField] private Vector3 followDamping = new Vector3(0.3f, 0.3f, 0.3f);

        [Header("Shake Presets (Impulse Forces)")]
        [SerializeField] private float treeHitForce = 0.35f;
        [SerializeField] private float treeFellForce = 1.0f;
        [SerializeField] private float combatHitForce = 0.5f;
        [SerializeField] private float playerDamagedForce = 1.2f;

        private Transform _currentTarget;
        private Tween _zoomTween;

        public Transform CurrentTarget => _currentTarget;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            InitializeComponents();
        }

        private void Start()
        {
            // If testing standalone/offline and no target registered yet, auto-find the player in the scene
            if (_currentTarget == null)
            {
                var player = Object.FindFirstObjectByType<Woodsmen.Players.LocomotionController>();
                if (player != null)
                {
                    SetTarget(player.transform, true);
                }
            }
        }

        private void OnDestroy()
        {
            if (_zoomTween.isAlive)
            {
                _zoomTween.Stop();
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnValidate()
        {
            ApplyFraming();
        }

        /// <summary>
        /// Applies tilt angle, follow offset, FOV, and damping to Cinemachine components.
        /// Updates live in Editor when adjusting sliders.
        /// </summary>
        public void ApplyFraming()
        {
            transform.rotation = Quaternion.Euler(tiltAngle, 0f, 0f);

            if (cinemachineFollow != null)
            {
                cinemachineFollow.FollowOffset = followOffset;
                cinemachineFollow.TrackerSettings.PositionDamping = followDamping;
            }

            if (cinemachineCam != null)
            {
                LensSettings lens = cinemachineCam.Lens;
                lens.FieldOfView = fieldOfView;
                cinemachineCam.Lens = lens;
            }
        }

        private void InitializeComponents()
        {
            if (cinemachineCam == null)
            {
                gameObject.TryGetComponent(out cinemachineCam);
            }

            if (cinemachineFollow == null && cinemachineCam != null)
            {
                cinemachineFollow = cinemachineCam.GetComponent<CinemachineFollow>();
            }

            if (impulseSource == null)
            {
                gameObject.TryGetComponent(out impulseSource);
            }

            // Ensure Main Camera has CinemachineBrain
            Camera mainCam = Camera.main;
            if (mainCam != null && !mainCam.TryGetComponent(out CinemachineBrain _))
            {
                mainCam.gameObject.AddComponent<CinemachineBrain>();
            }

            ApplyFraming();
        }

        #region Target Management (Multiplayer Safe)

        /// <summary>
        /// Sets the camera follow target. Should only be called for the local player.
        /// Remote players should NEVER register with CameraManager.
        /// </summary>
        public void SetTarget(Transform target, bool snapImmediately = false)
        {
            if (target == null) return;

            _currentTarget = target;

            if (cinemachineCam != null)
            {
                cinemachineCam.Follow = target;

                if (snapImmediately)
                {
                    // Snap camera directly to target position + offset without dragging
                    Vector3 targetCamPos = target.position + (cinemachineFollow != null ? cinemachineFollow.FollowOffset : followOffset);
                    cinemachineCam.OnTargetObjectWarped(target, target.position - transform.position);
                    transform.position = targetCamPos;
                }
            }
        }

        /// <summary>
        /// Clears current target if it matches the specified transform.
        /// </summary>
        public void ClearTarget(Transform target = null)
        {
            if (target == null || _currentTarget == target)
            {
                _currentTarget = null;
                if (cinemachineCam != null)
                {
                    cinemachineCam.Follow = null;
                }
            }
        }

        #endregion

        #region Screen Shake (Multiplayer Filtered - No Coroutines)

        /// <summary>
        /// Triggers a strictly local camera shake.
        /// Remote actions do NOT trigger this, ensuring other players' actions don't shake your screen.
        /// </summary>
        public void Shake(float force = 1f)
        {
            if (impulseSource != null)
            {
                impulseSource.GenerateImpulseWithForce(force);
            }
        }

        /// <summary>
        /// Triggers a directional shake (e.g. impact recoil direction).
        /// </summary>
        public void ShakeDirectional(Vector3 direction, float force = 1f)
        {
            if (impulseSource != null)
            {
                impulseSource.GenerateImpulseWithVelocity(direction.normalized * force);
            }
        }

        /// <summary>
        /// Triggers a shake with distance falloff relative to the local player.
        /// If a world event (e.g. tree crashing) occurs far away from the local player,
        /// this prevents the camera from shaking inappropriately.
        /// </summary>
        public void ShakeAtPosition(Vector3 worldPosition, float force = 1f, float maxDistance = 15f)
        {
            if (_currentTarget == null || impulseSource == null) return;

            float distance = Vector3.Distance(worldPosition, _currentTarget.position);
            if (distance >= maxDistance) return;

            // Linear distance attenuation (1.0 at epicenter to 0.0 at maxDistance)
            float attenuation = 1f - (distance / maxDistance);
            float attenuatedForce = force * attenuation;

            if (attenuatedForce > 0.05f)
            {
                impulseSource.GenerateImpulseAtPositionWithVelocity(
                    worldPosition,
                    Vector3.down * attenuatedForce
                );
            }
        }

        // --- Standard Preset Helpers ---

        public void ShakeTreeHit() => Shake(treeHitForce);
        public void ShakeCombatHit() => Shake(combatHitForce);
        public void ShakePlayerDamaged() => Shake(playerDamagedForce);
        public void ShakeTreeFell(Vector3 treeWorldPosition) => ShakeAtPosition(treeWorldPosition, treeFellForce, 18f);

        #endregion

        #region Procedural Zoom / FOV (PrimeTween - Zero GC)

        /// <summary>
        /// Smoothly zooms the camera in or out using PrimeTween (NO Coroutines).
        /// Adjusts the vertical offset of CinemachineFollow.
        /// </summary>
        public void SetZoom(float targetHeight, float duration = 0.4f, Ease ease = Ease.OutQuad)
        {
            if (cinemachineFollow == null) return;

            if (_zoomTween.isAlive)
            {
                _zoomTween.Stop();
            }

            float currentY = cinemachineFollow.FollowOffset.y;

            _zoomTween = Tween.Custom(currentY, targetHeight, duration: duration, onValueChange: val =>
            {
                Vector3 offset = cinemachineFollow.FollowOffset;
                offset.y = val;
                cinemachineFollow.FollowOffset = offset;
            }, ease: ease);
        }

        /// <summary>
        /// Resets the zoom back to default top-down framing.
        /// </summary>
        public void ResetZoom(float duration = 0.4f)
        {
            SetZoom(followOffset.y, duration);
        }

        #endregion
    }
}
