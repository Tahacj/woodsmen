using System;
using System.Collections.Generic;
using UnityEngine;
using Woodsmen.Players;

namespace Woodsmen.Shop
{
    /// <summary>
    /// Stores and tracks persistent player upgrades and attributes purchased from shops.
    /// Manages stat multipliers (e.g. Attack Speed, Chopping Speed) and keeps combat controllers synchronized.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerUpgrades : MonoBehaviour
    {
        [Header("Physical Enhancements")]
        [Tooltip("Attack speed multiplier (1.0 = normal, 1.25 = +25% faster).")]
        [SerializeField] private float attackSpeedMultiplier = 1.0f;

        [Tooltip("Chopping speed multiplier for Lumberjack (1.0 = normal, 1.3 = +30% faster).")]
        [SerializeField] private float choppingSpeedMultiplier = 1.0f;

        [SerializeField] private List<string> purchasedUpgradeList = new List<string>();
        private readonly HashSet<string> _purchasedUpgradeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public float AttackSpeedMultiplier => attackSpeedMultiplier;
        public float ChoppingSpeedMultiplier => choppingSpeedMultiplier;

        public event Action<string> OnUpgradeApplied;

        public bool HasUpgrade(string upgradeId)
        {
            if (string.IsNullOrEmpty(upgradeId)) return false;
            return _purchasedUpgradeIds.Contains(upgradeId) || purchasedUpgradeList.Contains(upgradeId);
        }

        /// <summary>
        /// Registers a purchased upgrade identifier.
        /// </summary>
        public void RegisterUpgrade(string upgradeId)
        {
            if (string.IsNullOrEmpty(upgradeId)) return;
            _purchasedUpgradeIds.Add(upgradeId);
            if (!purchasedUpgradeList.Contains(upgradeId))
            {
                purchasedUpgradeList.Add(upgradeId);
            }
            OnUpgradeApplied?.Invoke(upgradeId);
        }

        /// <summary>
        /// Boosts attack speed and updates PlayerCombatController immediately.
        /// </summary>
        public void ApplyAttackSpeedBoost(float boost)
        {
            attackSpeedMultiplier += boost;

            var combatController = GetComponentInChildren<Woodsmen.Combat.Weapons.PlayerCombatController>() 
                                ?? GetComponent<Woodsmen.Combat.Weapons.PlayerCombatController>();
            if (combatController != null)
            {
                combatController.AttackSpeedMultiplier = attackSpeedMultiplier;
            }

            Debug.Log($"<color=#eab308><b>[PlayerUpgrades]</b> Attack Speed boosted by +{boost * 100:F0}% (Now {attackSpeedMultiplier:F2}x)</color>");
        }

        /// <summary>
        /// Boosts chopping speed and updates LumberjackChopping immediately.
        /// </summary>
        public void ApplyChoppingSpeedBoost(float boost)
        {
            choppingSpeedMultiplier += boost;

            var lumberjackChopping = GetComponentInChildren<LumberjackChopping>() ?? GetComponent<LumberjackChopping>();
            if (lumberjackChopping != null)
            {
                lumberjackChopping.ChopSpeedMultiplier = choppingSpeedMultiplier;
                
                // Assuming base cooldown is 0.35f, decrease it proportionally to the speed increase
                lumberjackChopping.ChopCooldown = 0.35f / choppingSpeedMultiplier;
            }

            Debug.Log($"<color=#10b981><b>[PlayerUpgrades]</b> Chopping Speed boosted by +{boost * 100:F0}% (Now {choppingSpeedMultiplier:F2}x)</color>");
        }

        /// <summary>
        /// Applies an upgrade or attribute from a purchased shop item.
        /// </summary>
        public void ApplyUpgrade(ShopItemDefinition item)
        {
            if (item == null) return;
            RegisterUpgrade(item.Id);
        }
    }
}
