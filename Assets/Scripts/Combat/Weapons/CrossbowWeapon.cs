using System;
using UnityEngine;

namespace Woodsmen.Combat.Weapons
{
    /// <summary>
    /// Modular crossbow weapon implementing IWeapon.
    /// Handles firing cadence, bolt spawning, recoil feedback, and muzzle alignment.
    /// </summary>
    [Serializable]
    public class CrossbowWeapon : IWeapon
    {
        [Header("Weapon Identity")]
        [SerializeField] private string weaponId = "crossbow";
        [SerializeField] private string displayName = "Hunter's Crossbow";

        [Header("Combat Stats")]
        [Tooltip("Damage dealt per projectile hit.")]
        [SerializeField] private float damage = 40f;

        [Tooltip("Minimum time in seconds between consecutive shots.")]
        [SerializeField] private float fireCooldown = 0.4f;

        [Tooltip("Velocity of the bolt in units per second.")]
        [SerializeField] private float boltSpeed = 28f;

        [Tooltip("Movement speed multiplier applied to locomotion while firing.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float attackMovementMultiplier = 0.5f;

        [Tooltip("Duration in seconds that the character is considered in active attack state after firing.")]
        [SerializeField] private float activeAttackWindow = 0.25f;

        [Header("Visuals & Audio")]
        [Tooltip("Prefab for the bolt projectile.")]
        [SerializeField] private CrossbowBolt boltPrefab;

        [Tooltip("Spawn offset relative to the wielder's transform (chest/eye height, forward).")]
        [SerializeField] private Vector3 muzzleOffset = new Vector3(0f, 1.15f, 0.9f);

        [Tooltip("Sound clips played when a bolt is fired.")]
        [SerializeField] private AudioClip[] fireAudioClips;

        [Tooltip("Optional muzzle flash or smoke VFX prefab.")]
        [SerializeField] private GameObject muzzleVfxPrefab;

        [Tooltip("Optional visual crossbow model instantiated into the hand socket.")]
        [SerializeField] private GameObject visualCrossbowPrefab;

        // Runtime state
        private GameObject _wielder;
        private Transform _weaponSocket;
        private GameObject _spawnedVisualWeapon;
        private AudioSource _audioSource;
        private Animator _animator;
        private PlayerCombatController _combatController;

        private float _cooldownTimer;
        private float _attackActiveTimer;

        // Cached Animator hash for trigger/bool
        private static readonly int ShootHash = Animator.StringToHash("Shoot");
        private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");

        public string WeaponId => weaponId;
        public string DisplayName => displayName;
        public bool IsAttackActive => _attackActiveTimer > 0f;
        public float AttackMovementMultiplier => attackMovementMultiplier;
        public float Damage => damage;
        public float BoltSpeed => boltSpeed;
        public CrossbowBolt BoltPrefab => boltPrefab;
        public Vector3 MuzzleOffset => muzzleOffset;

        public float AttackSpeedMultiplier { get; set; } = 1.0f;

        public void SetBoltPrefab(CrossbowBolt prefab) => boltPrefab = prefab;

        public void OnEquip(GameObject wielder, Transform weaponSocket)
        {
            _wielder = wielder;
            _weaponSocket = weaponSocket;

            if (_wielder != null)
            {
                _wielder.TryGetComponent(out _audioSource);
                if (_audioSource == null)
                {
                    _audioSource = _wielder.AddComponent<AudioSource>();
                    _audioSource.playOnAwake = false;
                    _audioSource.spatialBlend = 0.5f;
                }

                _wielder.TryGetComponent(out _animator);
                _wielder.TryGetComponent(out _combatController);
            }

            // Spawn visual weapon model if configured
            if (visualCrossbowPrefab != null && _weaponSocket != null)
            {
                _spawnedVisualWeapon = UnityEngine.Object.Instantiate(visualCrossbowPrefab, _weaponSocket);
                _spawnedVisualWeapon.transform.localPosition = Vector3.zero;
                _spawnedVisualWeapon.transform.localRotation = Quaternion.identity;
            }
        }

        public void OnUnequip()
        {
            if (_spawnedVisualWeapon != null)
            {
                UnityEngine.Object.Destroy(_spawnedVisualWeapon);
                _spawnedVisualWeapon = null;
            }

            _wielder = null;
            _weaponSocket = null;
            _combatController = null;
        }

        public void OnAttackStart()
        {
            TryFire();
        }

        public void OnAttackHold()
        {
            // Continuous firing while button is held down
            TryFire();
        }

        public void OnAttackRelease()
        {
            // Release hook for charge-based weapons; crossbow fires instantly
        }

        public void TickWeapon(float deltaTime)
        {
            float scaledDelta = deltaTime * AttackSpeedMultiplier;

            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= scaledDelta;
            }

            if (_attackActiveTimer > 0f)
            {
                _attackActiveTimer -= scaledDelta;
                if (_attackActiveTimer <= 0f && _animator != null)
                {
                    _animator.SetBool(IsAttackingHash, false);
                }
            }
        }

        private bool TryFire()
        {
            if (_cooldownTimer > 0f) return false;
            if (_wielder == null) return false;

            _cooldownTimer = fireCooldown;
            _attackActiveTimer = activeAttackWindow;

            // Trigger animator state
            if (_animator != null)
            {
                _animator.SetBool(IsAttackingHash, true);
                _animator.SetTrigger(ShootHash);
            }

            // Play firing SFX
            PlayFireSound();

            // Calculate spawn position and aim direction
            Vector3 spawnPos = _wielder.transform.TransformPoint(muzzleOffset);
            Vector3 shootDirection = _wielder.transform.forward;
            shootDirection.y = 0f;
            shootDirection.Normalize();

            // Accurate 3D Aiming: if cursor is hovering over an enemy / damageable, aim directly at it!
            Camera cam = Camera.main;
            if (cam != null && _wielder.TryGetComponent(out Woodsmen.Players.PlayerInputReader inputReader))
            {
                Ray ray = cam.ScreenPointToRay(inputReader.PointerPosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.GetComponentInParent<Woodsmen.Combat.IDamageable>() != null)
                    {
                        Vector3 aimDir = (hit.point - spawnPos).normalized;
                        if (Vector3.Dot(aimDir, _wielder.transform.forward) > 0.1f)
                        {
                            shootDirection = aimDir;
                        }
                    }
                }
            }

            // Optional muzzle flash
            if (muzzleVfxPrefab != null)
            {
                GameObject vfx = UnityEngine.Object.Instantiate(muzzleVfxPrefab, spawnPos, Quaternion.LookRotation(shootDirection));
                UnityEngine.Object.Destroy(vfx, 1f);
            }

            // Delegate networked or offline projectile spawn to PlayerCombatController
            if (_combatController != null)
            {
                _combatController.FireProjectile(spawnPos, shootDirection, damage, boltSpeed * AttackSpeedMultiplier);
            }

            return true;
        }

        private void PlayFireSound()
        {
            if (_audioSource == null || fireAudioClips == null || fireAudioClips.Length == 0) return;

            AudioClip clip = fireAudioClips[UnityEngine.Random.Range(0, fireAudioClips.Length)];
            if (clip != null)
            {
                _audioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
                _audioSource.PlayOneShot(clip, 0.9f);
            }
        }
    }
}
