using UnityEngine;
using Woodsmen.Inventory;

namespace Woodsmen.Shop.Actions
{
    public enum StatUpgradeType
    {
        AttackSpeed = 0,
        ChoppingSpeed = 1
    }

    /// <summary>
    /// Modular shop action for permanent passive physical stat enhancements.
    /// Directly boosts attack speed or tree chopping speed on the character.
    /// </summary>
    [CreateAssetMenu(fileName = "NewStatUpgradeAction", menuName = "Woodsmen/Shop/Actions/Stat Upgrade Action", order = 31)]
    public class StatUpgradeAction : ShopItemAction
    {
        [Header("Stat Enhancement")]
        [SerializeField] private StatUpgradeType upgradeType = StatUpgradeType.AttackSpeed;
        [SerializeField] private float boostMultiplier = 0.25f;

        [Header("Summary Display")]
        [SerializeField] private string summary = "+25% Attack Speed";

        public override string EffectSummary => !string.IsNullOrEmpty(summary) ? summary : $"+{boostMultiplier * 100:F0}% {upgradeType}";

        public override void Execute(PlayerInventory buyer, ShopItemDefinition item)
        {
            if (buyer == null) return;

            var upgrades = buyer.GetComponentInParent<PlayerUpgrades>() ??
                           buyer.GetComponent<PlayerUpgrades>() ??
                           buyer.gameObject.AddComponent<PlayerUpgrades>();

            upgrades.RegisterUpgrade(item != null ? item.Id : upgradeType.ToString());

            if (upgradeType == StatUpgradeType.AttackSpeed)
            {
                upgrades.ApplyAttackSpeedBoost(boostMultiplier);
            }
            else if (upgradeType == StatUpgradeType.ChoppingSpeed)
            {
                upgrades.ApplyChoppingSpeedBoost(boostMultiplier);
            }

            Debug.Log($"<color=#10b981><b>[ShopAction]</b> Applied {upgradeType} boost (+{boostMultiplier * 100:F0}%) to {buyer.name}!</color>");
        }

        public void Configure(StatUpgradeType type, float boost, string displaySummary)
        {
            upgradeType = type;
            boostMultiplier = boost;
            summary = displaySummary;
        }
    }
}
