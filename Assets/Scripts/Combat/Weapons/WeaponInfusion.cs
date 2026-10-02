using UnityEngine;
using Woodsmen.Combat.Effects;

namespace Woodsmen.Combat.Weapons
{
    public enum InfusionType
    {
        None = 0,
        Fire = 1,
        Frost = 2
    }

    /// <summary>
    /// Professional weapon modifier component attached to characters and their weapons.
    /// Manages:
    /// - Active weapon visuals (e.g. flaming axe head, icy crossbow glow).
    /// - Delivery of elemental bonus damage upon weapon impact.
    /// - Status effect application (e.g. burn debuff with Fire VFX on hit enemies).
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponInfusion : MonoBehaviour
    {
        [Header("Infusion State")]
        [SerializeField] private InfusionType currentType = InfusionType.None;
        [SerializeField] private float bonusDamage = 15f;
        [SerializeField] private float effectDuration = 3.5f;
        [SerializeField] private float tickDamage = 5f;
        [SerializeField] private float slowPercentage = 0.25f;

        [Header("Visual Effects")]
        [Tooltip("Transform of the weapon blade/head where the visual aura attaches. If null, uses this transform.")]
        [SerializeField] private Transform weaponAttachmentBone;

        [Tooltip("VFX prefab attached to the weapon itself (flaming blade, frost aura).")]
        [SerializeField] private GameObject weaponAuraVfxPrefab;

        [Tooltip("VFX prefab spawned on struck enemies (impact flash effect on enemy body).")]
        [SerializeField] private GameObject targetHitVfxPrefab;

        [Tooltip("VFX prefab parented to the enemy and kept alive for the full burn duration (moves with the enemy).")]
        [SerializeField] private GameObject enemyBurnVfxPrefab;

        private GameObject _spawnedWeaponVfx;

        public InfusionType CurrentType => currentType;
        public float BonusDamage => bonusDamage;
        public GameObject TargetHitVfxPrefab => targetHitVfxPrefab;

        private void Awake()
        {
            if (weaponAttachmentBone == null)
            {
                // Auto-detect weapon transform or axe head if available
                var axeHead = transform.Find("Axe_Head") ?? transform.Find("WeaponSocket") ?? transform.Find("weapon");
                weaponAttachmentBone = axeHead != null ? axeHead : transform;
            }

            CleanupCharacterVfx();
        }

        /// <summary>
        /// Cleans up any previously attached visual effects or lights from the character.
        /// </summary>
        public void CleanupCharacterVfx()
        {
            if (_spawnedWeaponVfx != null)
            {
                Destroy(_spawnedWeaponVfx);
                _spawnedWeaponVfx = null;
            }

            // Remove any lingering procedural flame or frost objects that might exist on this character
            var children = GetComponentsInChildren<Transform>(true);
            foreach (var child in children)
            {
                if (child != null && child != transform && (child.name == "ProceduralWeaponFlame" || child.name == "ProceduralWeaponFrost"))
                {
                    Destroy(child.gameObject);
                }
            }
        }

        /// <summary>
        /// Applies or switches the weapon infusion.
        /// Does NOT attach any VFX/lights to the character - elemental effects apply to enemies only.
        /// </summary>
        public void ApplyInfusion(
            InfusionType type,
            GameObject weaponAuraPrefab,
            GameObject hitVfxPrefab,
            GameObject burnBodyVfxPrefab,
            float extraDamage,
            float duration,
            float dotDamage,
            float slow = 0f)
        {
            currentType = type;
            weaponAuraVfxPrefab = weaponAuraPrefab;
            targetHitVfxPrefab = hitVfxPrefab;
            enemyBurnVfxPrefab = burnBodyVfxPrefab;
            bonusDamage = extraDamage;
            effectDuration = duration;
            tickDamage = dotDamage;
            slowPercentage = slow;

            // Ensure no visual aura or light is attached to the character
            CleanupCharacterVfx();

            Debug.Log($"<color=#f59e0b><b>[WeaponInfusion]</b> Infused weapon with {type}! (+{bonusDamage} Damage, effects apply to enemies on hit)</color>");
        }

        /// <summary>
        /// Called when the weapon strikes a target (from WarriorCombat, LumberjackChopping, or CrossbowBolt).
        /// Applies the elemental hit effect, bonus damage, and status debuff directly to the struck target.
        /// </summary>
        public void OnWeaponHit(IDamageable target, Vector3 hitPoint, Vector3 hitDirection, GameObject wielder)
        {
            if (target == null || currentType == InfusionType.None) return;

            // 1. Bonus elemental damage
            if (bonusDamage > 0f && !target.IsDead)
            {
                target.TakeDamage(bonusDamage, hitPoint, hitDirection, wielder);
            }

            // 2. Elemental status effect dynamically applied only to the hit enemy
            if (target is Component comp && comp != null)
            {
                GameObject enemyGo = comp.gameObject;

                if (currentType == InfusionType.Fire)
                {
                    BurnDebuff.Apply(enemyGo, targetHitVfxPrefab, enemyBurnVfxPrefab, effectDuration, tickDamage, wielder);
                }
                else if (currentType == InfusionType.Frost)
                {
                    FrostDebuff.Apply(enemyGo, targetHitVfxPrefab, effectDuration, slowPercentage, wielder);
                }
            }
        }

        private void OnDestroy()
        {
            if (_spawnedWeaponVfx != null)
            {
                Destroy(_spawnedWeaponVfx);
            }
        }
    }
}
