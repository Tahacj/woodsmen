using PrimeTween;
using Unity.Cinemachine;
using UnityEngine;
using Woodsmen.Utilities;

namespace Woodsmen.CameraSystem
{
    /// <summary>
    /// Centralized Camera Manager and Brain for the top-down perspective.
    /// Manages CinemachineCamera framing, smooth target tracking for the local player,
    /// and provides high-performance, multiplayer-isolated camera shakes via PrimeTween and Cinemachine Impulse (Zero GC).
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

        [Header("PrimeTween Procedural Camera Shake (Zero-GC)")]
        [Tooltip("Use PrimeTween procedural shake on Cinemachine FollowOffset (lightweight, zero noise-asset dependencies, 100% reliable).")]
        [SerializeField] private bool usePrimeTweenShake = true;

        [Tooltip("Tree hit camera shake intensity (recoil offset in meters).")]
        [SerializeField] private float treeHitIntensity = 0.22f;

        [Tooltip("Tree hit camera shake duration in seconds.")]
        [SerializeField] private float treeHitDuration = 0.15f;

        [Tooltip("Tree collapse thud shake intensity.")]
        [SerializeField] private float treeFellIntensity = 0.50f;

        [Tooltip("Tree collapse thud shake duration in seconds.")]
        [SerializeField] private float treeFellDuration = 0.30f;

        [Tooltip("Player damaged shake intensity.")]
        [SerializeField] private float playerDamagedIntensity = 0.45f;

        [Tooltip("Vibration frequency (oscillations per second). Higher = sharper impact vibration.")]
        [SerializeField] private int defaultShakeFrequency = 22;

        [Header("Cinemachine Impulse Presets (Fallback/Layer)")]
        [SerializeField] private float treeHitForce = 0.35f;
        [SerializeField] private float treeFellForce = 1.0f;
        [SerializeField] private float combatHitForce = 0.5f;
        [SerializeField] private float playerDamagedForce = 1.2f;

        private Transform _currentTarget;
        private Tween _zoomTween;
        private Tween _shakeTween;

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
            if (_zoomTween.isAlive) _zoomTween.Stop();
            if (_shakeTween.isAlive) _shakeTween.Stop();

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

        #region Screen Shake (PrimeTween & Cinemachine Impulse)

        /// <summary>
        /// PrimeTween Procedural Camera Shake (Zero GC, 100% reliable, zero asset dependencies).
        /// Oscillates the Cinemachine FollowOffset with a decaying damped harmonic sine wave.
        /// </summary>
        public void ShakePrimeTween(float intensity = 0.22f, float duration = 0.15f, int frequency = 22, Vector3 direction = default)
        {
            if (cinemachineFollow == null) return;

            if (_shakeTween.isAlive)
            {
                _shakeTween.Stop();
            }

            // Planar recoil direction (default to random angle if zero)
            if (direction == default || direction.sqrMagnitude < 0.001f)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            else
            {
                direction.y = 0f;
                direction.Normalize();
            }

            Vector3 baseOffset = followOffset;
            baseOffset.y = cinemachineFollow.FollowOffset.y; // Preserve active zoom level

            Vector3 perpDirection = new Vector3(-direction.z, 0f, direction.x);

            _shakeTween = Tween.Custom(0f, 1f, duration: duration, onValueChange: progress =>
            {
                if (cinemachineFollow == null) return;

                float decay = 1f - progress;
                float sine = Mathf.Sin(progress * frequency * Mathf.PI);
                float perpSine = Mathf.Cos(progress * frequency * 1.3f * Mathf.PI) * 0.4f;

                Vector3 shakeDelta = (direction * sine + perpDirection * perpSine) * (intensity * decay);
                cinemachineFollow.FollowOffset = baseOffset + shakeDelta;
            }, ease: Ease.Linear).OnComplete(() =>
            {
                if (cinemachineFollow != null)
                {
                    Vector3 resetOffset = followOffset;
                    resetOffset.y = cinemachineFollow.FollowOffset.y;
                    cinemachineFollow.FollowOffset = resetOffset;
                }
            });
        }

        /// <summary>
        /// General screen shake. Shakes via PrimeTween and Cinemachine Impulse simultaneously.
        /// </summary>
        public void Shake(float force = 1f)
        {
            if (usePrimeTweenShake)
            {
                ShakePrimeTween(force * 0.35f, 0.16f, defaultShakeFrequency);
            }

            if (impulseSource != null)
            {
                impulseSource.GenerateImpulseWithForce(force);
            }
        }

        /// <summary>
        /// Directional screen shake.
        /// </summary>
        public void ShakeDirectional(Vector3 direction, float force = 1f)
        {
            if (usePrimeTweenShake)
            {
                ShakePrimeTween(force * 0.35f, 0.16f, defaultShakeFrequency, direction);
            }

            if (impulseSource != null)
            {
                impulseSource.GenerateImpulseWithVelocity(direction.normalized * force);
            }
        }

        /// <summary>
        /// Triggered when the Lumberjack chops a tree.
        /// Produces a snappy recoil punch away from the chopped tree.
        /// Distance-attenuated to avoid shaking if the local player is far away.
        /// </summary>
        public void ShakeTreeHit(Vector3 treeWorldPosition = default)
        {
            Vector3 recoilDir = Vector3.zero;
            float attenuation = 1f;

            if (_currentTarget != null && treeWorldPosition != default)
            {
                float distance = Vector3.Distance(_currentTarget.position, treeWorldPosition);
                if (distance > 15f) return; // Isolated from distant chops

                attenuation = Mathf.Clamp01(1f - (distance / 15f));
                recoilDir = (_currentTarget.position - treeWorldPosition).normalized;
            }

            if (usePrimeTweenShake)
            {
                ShakePrimeTween(treeHitIntensity * attenuation, treeHitDuration, defaultShakeFrequency, recoilDir);
            }

            if (impulseSource != null)
            {
                impulseSource.GenerateImpulseWithForce(treeHitForce * attenuation);
            }
        }

        /// <summary>
        /// Triggered when a tree collapses to the ground.
        /// Produces a heavier ground thud with distance attenuation.
        /// </summary>
        public void ShakeTreeFell(Vector3 treeWorldPosition)
        {
            if (_currentTarget == null) return;

            float distance = Vector3.Distance(treeWorldPosition, _currentTarget.position);
            if (distance >= 18f) return;

            float attenuation = 1f - (distance / 18f);

            if (usePrimeTweenShake)
            {
                ShakePrimeTween(treeFellIntensity * attenuation, treeFellDuration, 14, Vector3.forward);
            }

            if (impulseSource != null)
            {
                impulseSource.GenerateImpulseAtPositionWithVelocity(
                    treeWorldPosition,
                    Vector3.down * (treeFellForce * attenuation)
                );
            }
        }

        /// <summary>
        /// Triggered when the local player takes damage.
        /// </summary>
        public void ShakePlayerDamaged()
        {
            if (usePrimeTweenShake)
            {
                ShakePrimeTween(playerDamagedIntensity, 0.22f, 26);
            }

            if (impulseSource != null)
            {
                impulseSource.GenerateImpulseWithForce(playerDamagedForce);
            }
        }

        /// <summary>
        /// Triggered on melee combat hits.
        /// </summary>
        public void ShakeCombatHit()
        {
            if (usePrimeTweenShake)
            {
                ShakePrimeTween(treeHitIntensity * 1.3f, 0.16f, defaultShakeFrequency);
            }

            if (impulseSource != null)
            {
                impulseSource.GenerateImpulseWithForce(combatHitForce);
            }
        }

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
