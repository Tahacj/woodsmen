using Mirror;
using UnityEngine;
using Woodsmen.Combat;
using Woodsmen.Players;

namespace Woodsmen.Combat.Weapons
{
    /// <summary>
    /// Core networked combat controller for player characters.
    /// Manages equippable weapons, translates player input, implements ICombatController,
    /// and synchronizes projectile dispatches across Mirror multiplayer sessions with 100% offline single-player support.
    /// </summary>
    public class PlayerCombatController : NetworkBehaviour, ICombatController
    {
        [Header("Equipped Weapon Configuration")]
        [Tooltip("Default crossbow weapon settings for this character.")]
        [SerializeField] private CrossbowWeapon defaultCrossbow = new CrossbowWeapon();

        [Tooltip("Transform socket bone (e.g. jointItemR) where weapons attach.")]
        [SerializeField] private Transform weaponSocket;

        [Tooltip("Prefab for the crossbow bolt projectile.")]
        [SerializeField] private CrossbowBolt boltPrefab;

        // Component References
        private PlayerInputReader _inputReader;
        private CharacterHealth _characterHealth;
        private Animator _animator;
        private IWeapon _currentWeapon;

        #region Public Properties (ICombatController)

        public bool IsAttackActive => _currentWeapon != null && _currentWeapon.IsAttackActive;

        public float AttackMovementMultiplier => _currentWeapon != null ? _currentWeapon.AttackMovementMultiplier : 1.0f;

        public IWeapon CurrentWeapon => _currentWeapon;

        public float AttackSpeedMultiplier
        {
            get => _currentWeapon is CrossbowWeapon cw ? cw.AttackSpeedMultiplier : 1.0f;
            set
            {
                if (_currentWeapon is CrossbowWeapon cw)
                {
                    cw.AttackSpeedMultiplier = value;
                }
            }
        }

        public Transform WeaponSocket
        {
            get => weaponSocket;
            set => weaponSocket = value;
        }

        public CrossbowBolt BoltPrefab
        {
            get => boltPrefab;
            set
            {
                boltPrefab = value;
                if (defaultCrossbow != null && boltPrefab != null)
                {
                    defaultCrossbow.SetBoltPrefab(boltPrefab);
                }
            }
        }

        #endregion

        private bool IsLocallyControlled => isLocalPlayer || (!NetworkClient.active && !NetworkServer.active);

        private void Awake()
        {
            gameObject.TryGetComponent(out _inputReader);
            gameObject.TryGetComponent(out _characterHealth);
            gameObject.TryGetComponent(out _animator);

            // Locate socket if not explicitly assigned
            if (weaponSocket == null)
            {
                weaponSocket = FindWeaponSocket(transform);
            }

            // Fallback load bolt prefab if unassigned
            if (boltPrefab == null)
            {
                boltPrefab = Resources.Load<CrossbowBolt>("Combat/CrossbowBolt");
#if UNITY_EDITOR
                if (boltPrefab == null)
                {
                    boltPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<CrossbowBolt>("Assets/Prefabs/Combat/CrossbowBolt.prefab");
                }
#endif
            }

            if (boltPrefab != null && defaultCrossbow != null)
            {
                defaultCrossbow.SetBoltPrefab(boltPrefab);
            }
        }

        private void OnEnable()
        {
            if (_inputReader != null)
            {
                _inputReader.OnPrimaryActionStarted += HandlePrimaryActionStarted;
                _inputReader.OnPrimaryActionCanceled += HandlePrimaryActionCanceled;
            }
        }

        private void OnDisable()
        {
            if (_inputReader != null)
            {
                _inputReader.OnPrimaryActionStarted -= HandlePrimaryActionStarted;
                _inputReader.OnPrimaryActionCanceled -= HandlePrimaryActionCanceled;
            }

            if (_currentWeapon != null)
            {
                _currentWeapon.OnUnequip();
            }
        }

        private void Start()
        {
            // Equip default weapon on start
            if (_currentWeapon == null && defaultCrossbow != null)
            {
                EquipWeapon(defaultCrossbow);
            }
        }

        private void Update()
        {
            // Death Guard
            if (_characterHealth != null && _characterHealth.IsDead) return;

            // Tick weapon cooldowns and animations on all clients
            _currentWeapon?.TickWeapon(Time.deltaTime);

            if (!IsLocallyControlled) return;

            // Suppress attack input if pointer is hovering over UI elements
            if (_inputReader != null && _inputReader.IsPrimaryActionHeld)
            {
                if (Woodsmen.Inventory.UI.UniversalInventoryUI.IsOpen &&
                    UnityEngine.EventSystems.EventSystem.current != null &&
                    UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                {
                    return;
                }

                _currentWeapon?.OnAttackHold();
            }
        }

        #region Weapon Lifecycle

        /// <summary>
        /// Equips a new weapon, replacing any currently equipped weapon.
        /// </summary>
        public void EquipWeapon(IWeapon newWeapon)
        {
            if (_currentWeapon != null)
            {
                _currentWeapon.OnUnequip();
            }

            _currentWeapon = newWeapon;

            if (_currentWeapon != null)
            {
                _currentWeapon.OnEquip(gameObject, weaponSocket);
            }
        }

        #endregion

        #region Input Event Handlers

        private void HandlePrimaryActionStarted()
        {
            if (!IsLocallyControlled) return;
            if (_characterHealth != null && _characterHealth.IsDead) return;

            // Don't fire if clicking UI
            if (Woodsmen.Inventory.UI.UniversalInventoryUI.IsOpen &&
                UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            _currentWeapon?.OnAttackStart();
        }

        private void HandlePrimaryActionCanceled()
        {
            if (!IsLocallyControlled) return;
            _currentWeapon?.OnAttackRelease();
        }

        #endregion

        #region Projectile Spawning & Mirror Multiplayer

        /// <summary>
        /// Dispatches a projectile forward from the weapon.
        /// Handles local prediction, server validation, and remote client replication.
        /// </summary>
        public void FireProjectile(Vector3 origin, Vector3 direction, float damage, float speed)
        {
            if (boltPrefab == null)
            {
                Debug.LogWarning("[Woodsmen.PlayerCombatController] Cannot fire: BoltPrefab is not assigned!");
                return;
            }

            // Standalone Offline Mode (100% playable without Mirror server)
            if (!NetworkClient.active && !NetworkServer.active)
            {
                SpawnBoltInstance(origin, direction, damage, speed, isServerSim: true);
                return;
            }

            // Mirror Multiplayer
            if (isServer)
            {
                // Host / Dedicated Server: Spawn authoritative bolt
                SpawnBoltInstance(origin, direction, damage, speed, isServerSim: true);
                RpcOnFireBolt(origin, direction, damage, speed);
            }
            else if (isLocalPlayer)
            {
                // Local Client: Spawn cosmetic bolt immediately for 0-latency feel, then notify server
                SpawnBoltInstance(origin, direction, damage, speed, isServerSim: false);
                CmdFireBolt(origin, direction);
            }
        }

        [Command]
        private void CmdFireBolt(Vector3 origin, Vector3 direction)
        {
            float damage = defaultCrossbow != null ? defaultCrossbow.Damage : 40f;
            float speed = defaultCrossbow != null ? defaultCrossbow.BoltSpeed : 28f;

            // Server runs authoritative physics & damage
            SpawnBoltInstance(origin, direction, damage, speed, isServerSim: true);

            // Replicate cosmetic projectile to other clients
            RpcOnFireBolt(origin, direction, damage, speed);
        }

        [ClientRpc(includeOwner = false)]
        private void RpcOnFireBolt(Vector3 origin, Vector3 direction, float damage, float speed)
        {
            // Remote clients render projectile flying
            SpawnBoltInstance(origin, direction, damage, speed, isServerSim: false);
        }

        private void SpawnBoltInstance(Vector3 origin, Vector3 direction, float damage, float speed, bool isServerSim)
        {
            CrossbowBolt prefab = GetValidBoltPrefab();
            if (prefab == null)
            {
                Debug.LogError("[Woodsmen.PlayerCombatController] Critical: Could not resolve bolt prefab!");
                return;
            }

            Quaternion rotation = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
            CrossbowBolt bolt = Instantiate(prefab, origin, rotation);
            bolt.gameObject.SetActive(true);
            bolt.Launch(gameObject, direction, damage, speed, isServerSim);
        }

        private CrossbowBolt GetValidBoltPrefab()
        {
            if (boltPrefab != null) return boltPrefab;

            boltPrefab = Resources.Load<CrossbowBolt>("Combat/CrossbowBolt");
            if (boltPrefab != null) return boltPrefab;

#if UNITY_EDITOR
            boltPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<CrossbowBolt>("Assets/Prefabs/Combat/CrossbowBolt.prefab");
            if (boltPrefab != null) return boltPrefab;
#endif

            // Emergency runtime fallback template if all assets are missing
            GameObject template = new GameObject("[CrossbowBolt_RuntimeFallback]");
            template.SetActive(false);
            DontDestroyOnLoad(template);

            var bolt = template.AddComponent<CrossbowBolt>();
            var rb = template.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var col = template.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.35f;

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "Visual";
            visual.transform.SetParent(template.transform, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.transform.localScale = new Vector3(0.06f, 0.4f, 0.06f);
            if (visual.TryGetComponent(out Collider c)) Destroy(c);

            var trail = template.AddComponent<TrailRenderer>();
            trail.time = 0.15f;
            trail.startWidth = 0.08f;
            trail.endWidth = 0.0f;

            boltPrefab = bolt;
            return boltPrefab;
        }

        #endregion

        private static Transform FindWeaponSocket(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "jointItemR")
                {
                    return t;
                }
            }
            return root;
        }
    }
}
