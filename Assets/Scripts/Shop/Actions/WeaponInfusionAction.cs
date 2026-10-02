using UnityEngine;
using Woodsmen.Combat.Weapons;
using Woodsmen.Inventory;
using Woodsmen.Players;

namespace Woodsmen.Shop.Actions
{
    /// <summary>
    /// Modular shop action for Weapon Infusions (Fire Infusion, Frostbite Rune).
    /// Infuses the character's weapon directly:
    /// - Attaches the visual elemental aura (e.g. fire/frost) to the wielder's weapon model.
    /// - Configures on-hit bonus damage and target debuffs (e.g. burning fire VFX on struck enemies).
    /// </summary>
    [CreateAssetMenu(fileName = "NewWeaponInfusionAction", menuName = "Woodsmen/Shop/Actions/Weapon Infusion Action", order = 30)]
    public class WeaponInfusionAction : ShopItemAction
    {
        [Header("Infusion Type & Stats")]
        [SerializeField] private InfusionType infusionType = InfusionType.Fire;
        [SerializeField] private float bonusDamage = 15f;
        [SerializeField] private float effectDuration = 3.5f;
        [SerializeField] private float burnTickDamage = 5f;
        [SerializeField] private float slowStrength = 0.25f;

        [Header("Visual Effects")]
        [Tooltip("VFX prefab attached directly to the character's weapon (e.g. flaming axe head, frost crossbow aura).")]
        [SerializeField] private GameObject weaponAuraVfxPrefab;

        [Tooltip("VFX prefab spawned on enemies when struck (impact flash effect).")]
        [SerializeField] private GameObject targetHitVfxPrefab;

        [Tooltip("VFX prefab parented to the enemy's center and kept alive for the full burn duration (moves with the enemy).")]
        [SerializeField] private GameObject enemyBurnVfxPrefab;

        [Header("Summary Display")]
        [SerializeField] private string summary = "+15 Fire Damage & Burning On-Hit";

        /// <summary>Read-only accessor used by the shop duplicate-purchase guard.</summary>
        public InfusionType InfusionType => infusionType;

        public override string EffectSummary => !string.IsNullOrEmpty(summary) ? summary : $"+{bonusDamage} {infusionType} Damage";

        public override void Execute(PlayerInventory buyer, ShopItemDefinition item)
        {
            if (buyer == null) return;

            // 1. Find or add WeaponInfusion on the player / weapon
            var infusion = buyer.GetComponentInChildren<WeaponInfusion>();
            if (infusion == null)
            {
                infusion = buyer.GetComponent<WeaponInfusion>();
            }
            if (infusion == null)
            {
                infusion = buyer.gameObject.AddComponent<WeaponInfusion>();
            }

            // 2. Infuse the weapon with visuals and on-hit behavior
            infusion.ApplyInfusion(
                infusionType,
                weaponAuraVfxPrefab,
                targetHitVfxPrefab,
                enemyBurnVfxPrefab,
                bonusDamage,
                effectDuration,
                burnTickDamage,
                slowStrength
            );

            // 3. If this is a Frost infusion, enable the Lumberjack's enemy-hit capability
            if (infusionType == InfusionType.Frost)
            {
                var chopping = buyer.GetComponentInChildren<LumberjackChopping>() ??
                               buyer.GetComponent<LumberjackChopping>();
                if (chopping != null)
                {
                    chopping.EnableFrostbiteEnemyHit(infusion);
                }
            }

            // 4. Keep PlayerUpgrades synchronized for HUD / stat tracking
            var upgrades = buyer.GetComponentInParent<PlayerUpgrades>() ??
                           buyer.GetComponent<PlayerUpgrades>() ??
                           buyer.gameObject.AddComponent<PlayerUpgrades>();
            upgrades.ApplyUpgrade(item);

            Debug.Log($"<color=#10b981><b>[ShopAction]</b> Applied {infusionType} Infusion to {buyer.name}'s weapon!</color>");
        }

        public void Configure(
            InfusionType type,
            float extraDamage,
            float duration,
            float tickDamage,
            float slow,
            string displaySummary,
            GameObject weaponVfx = null,
            GameObject hitVfx = null,
            GameObject burnBodyVfx = null)
        {
            infusionType = type;
            bonusDamage = extraDamage;
            effectDuration = duration;
            burnTickDamage = tickDamage;
            slowStrength = slow;
            summary = displaySummary;
            weaponAuraVfxPrefab = weaponVfx;
            targetHitVfxPrefab = hitVfx;
            enemyBurnVfxPrefab = burnBodyVfx;
        }
    }
}
