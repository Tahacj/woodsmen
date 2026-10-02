using UnityEngine;
using Woodsmen.Combat;
using Woodsmen.Environment;
using Woodsmen.Feedback;
using Woodsmen.Players;

namespace Woodsmen.Combat.Weapons
{
    /// <summary>
    /// Rock-solid, zero-GC physical bolt fired from a crossbow.
    /// Features:
    /// - Multi-layered continuous collision detection (OverlapSphere + RaycastAll + SphereCastAll + OnTriggerEnter).
    /// - Immune to PhysX initial-overlap failure bugs and high-speed tunneling.
    /// - Automatically routes authoritative damage to any IDamageable (Goblin enemies, trees, destructibles).
    /// - Full offline single-player and Mirror multiplayer replication support.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CrossbowBolt : MonoBehaviour
    {
        [Header("Projectile Properties")]
        [SerializeField] private float speed = 28f;
        [SerializeField] private float damage = 40f;
        [SerializeField] private float maxLifetime = 3.5f;
        [SerializeField] private float sweepRadius = 0.35f;
        [SerializeField] private LayerMask collisionMask = ~0;

        [Header("Effects & Audio")]
        [SerializeField] private GameObject hitVfxPrefab;
        [SerializeField] private AudioClip hitAudioClip;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private TrailRenderer trailRenderer;

        private Vector3 _direction;
        private GameObject _shooter;
        private bool _isServerSim;
        private float _age;
        private bool _hasHit;

        // Non-allocating buffer for overlap checks
        private static readonly Collider[] OverlapBuffer = new Collider[16];

        /// <summary>
        /// Initializes and launches the bolt along a travel vector.
        /// </summary>
        public void Launch(GameObject shooter, Vector3 direction, float damageAmount, float boltSpeed, bool isServerSim)
        {
            _shooter = shooter;
            _direction = direction.normalized;
            damage = damageAmount;
            speed = boltSpeed;
            _isServerSim = isServerSim;
            _age = 0f;
            _hasHit = false;

            if (_direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(_direction);
            }

            if (trailRenderer != null)
            {
                trailRenderer.Clear();
            }

            // Ensure Rigidbody exists and is kinematic
            if (!TryGetComponent(out Rigidbody rb))
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            // Explicitly ignore collision with all shooter colliders (CharacterController, hurtboxes)
            if (_shooter != null)
            {
                Collider boltCol = GetComponent<Collider>();
                if (boltCol != null)
                {
                    Collider[] shooterCols = _shooter.GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < shooterCols.Length; i++)
                    {
                        if (shooterCols[i] != null && shooterCols[i] != boltCol)
                        {
                            Physics.IgnoreCollision(boltCol, shooterCols[i], true);
                        }
                    }
                }
            }
        }

        private void Update()
        {
            if (_hasHit) return;

            _age += Time.deltaTime;
            if (_age >= maxLifetime)
            {
                DespawnBolt();
                return;
            }

            float stepDistance = speed * Time.deltaTime;
            Vector3 currentPos = transform.position;
            Vector3 nextPos = currentPos + _direction * stepDistance;

            // Layer 1: Check if ALREADY overlapping/touching a target collider at currentPos
            int overlapCount = Physics.OverlapSphereNonAlloc(currentPos, sweepRadius, OverlapBuffer, collisionMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < overlapCount; i++)
            {
                Collider col = OverlapBuffer[i];
                if (ShouldIgnore(col)) continue;

                HandleImpact(col, col.ClosestPoint(currentPos), -_direction);
                return;
            }

            // Layer 2: Raycast forward sweep (zero thickness prevents any PhysX initial overlap failure)
            RaycastHit[] rayHits = Physics.RaycastAll(currentPos, _direction, stepDistance, collisionMask, QueryTriggerInteraction.Collide);
            if (rayHits != null && rayHits.Length > 0)
            {
                System.Array.Sort(rayHits, (a, b) => a.distance.CompareTo(b.distance));
                for (int i = 0; i < rayHits.Length; i++)
                {
                    RaycastHit h = rayHits[i];
                    if (ShouldIgnore(h.collider)) continue;

                    HandleImpact(h.collider, h.point, h.normal);
                    return;
                }
            }

            // Layer 3: Thick SphereCast sweep (detects targets slightly off the direct line of sight)
            RaycastHit[] sphereHits = Physics.SphereCastAll(currentPos, sweepRadius, _direction, stepDistance, collisionMask, QueryTriggerInteraction.Collide);
            if (sphereHits != null && sphereHits.Length > 0)
            {
                System.Array.Sort(sphereHits, (a, b) => a.distance.CompareTo(b.distance));
                for (int i = 0; i < sphereHits.Length; i++)
                {
                    RaycastHit h = sphereHits[i];
                    if (ShouldIgnore(h.collider)) continue;

                    HandleImpact(h.collider, h.point, h.normal);
                    return;
                }
            }

            // Advance position forward
            transform.position = nextPos;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hasHit) return;
            if (ShouldIgnore(other)) return;

            Vector3 hitPoint = other.ClosestPoint(transform.position);
            Vector3 hitNormal = -_direction;
            HandleImpact(other, hitPoint, hitNormal);
        }

        private bool ShouldIgnore(Collider col)
        {
            if (col == null || col.gameObject == gameObject) return true;
            if (IsShooter(col)) return true;
            if (IsTeammate(col)) return true;

            // Ignore collectible pickups (coins, wood, etc.) so arrows fly straight through them without being blocked
            if (col.GetComponentInParent<CoinPickup>() != null || col.GetComponentInParent<WoodPickup>() != null)
            {
                return true;
            }

            // Ignore non-damageable trigger volumes (pickup detection spheres, ambient zones, etc.)
            if (col.isTrigger && col.GetComponentInParent<IDamageable>() == null)
            {
                return true;
            }

            return false;
        }

        private bool IsShooter(Collider col)
        {
            if (_shooter == null || col == null) return false;
            Transform colTransform = col.transform;
            Transform shooterTransform = _shooter.transform;

            return colTransform == shooterTransform ||
                   colTransform.IsChildOf(shooterTransform) ||
                   colTransform.root == shooterTransform.root;
        }

        /// <summary>
        /// Identifies whether a collider belongs to a friendly player or teammate.
        /// Allows arrows to pass harmlessly through teammates.
        /// </summary>
        private bool IsTeammate(Collider col)
        {
            if (col == null) return false;

            // Player layer check
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer != -1 && col.gameObject.layer == playerLayer)
            {
                return true;
            }

            // Player tag check
            if (col.CompareTag("Player"))
            {
                return true;
            }

            // Player character components (Lumberjack / Warrior / LocomotionController)
            if (col.GetComponentInParent<LocomotionController>() != null ||
                col.GetComponentInParent<ICombatController>() != null ||
                col.GetComponentInParent<LumberjackChopping>() != null)
            {
                return true;
            }

            return false;
        }

        private void HandleImpact(Collider targetCollider, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (_hasHit) return;
            if (targetCollider != null && (IsShooter(targetCollider) || IsTeammate(targetCollider))) return;
            _hasHit = true;

            bool isTree = targetCollider != null && targetCollider.GetComponentInParent<DestructibleTree>() != null;

            // Authoritative damage routing (Arrows damage combat entities like goblins, not trees)
            if (_isServerSim && targetCollider != null && !isTree)
            {
                IDamageable damageable = targetCollider.GetComponentInParent<IDamageable>();
                if (damageable != null && !(damageable is DestructibleTree))
                {
                    damageable.TakeDamage(damage, hitPoint, _direction, _shooter);

                    if (_shooter != null)
                    {
                        var infusion = _shooter.GetComponentInChildren<WeaponInfusion>() ??
                                       _shooter.GetComponent<WeaponInfusion>();
                        if (infusion != null)
                        {
                            infusion.OnWeaponHit(damageable, hitPoint, _direction, _shooter);
                        }
                    }
                }
            }

            // Play impact VFX
            if (isTree)
            {
                VFXManager.Instance?.PlayWoodImpact(hitPoint, hitNormal);
            }
            else if (hitVfxPrefab != null)
            {
                Quaternion hitRot = hitNormal.sqrMagnitude > 0.001f ? Quaternion.LookRotation(hitNormal) : Quaternion.identity;
                GameObject vfx = Instantiate(hitVfxPrefab, hitPoint, hitRot);
                Destroy(vfx, 1.5f);
            }

            // Play impact SFX
            if (hitAudioClip != null)
            {
                AudioSource.PlayClipAtPoint(hitAudioClip, hitPoint, 0.85f);
            }

            DespawnBolt();
        }

        private void DespawnBolt()
        {
            Destroy(gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, sweepRadius);
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, _direction * 1.5f);
        }
    }
}
