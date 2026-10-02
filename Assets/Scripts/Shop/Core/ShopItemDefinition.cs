using UnityEngine;
using Woodsmen.Inventory;

namespace Woodsmen.Shop
{
    /// <summary>
    /// ScriptableObject defining an item available for purchase in shops.
    /// Encapsulates pricing (wood + coins combined), role restrictions, classification,
    /// and polymorphic item or stat upgrade application.
    /// </summary>
    [CreateAssetMenu(fileName = "NewShopItem", menuName = "Woodsmen/Shop/Shop Item Definition", order = 20)]
    public class ShopItemDefinition : ScriptableObject
    {
        [Header("Identity & Display")]
        [Tooltip("Unique identifier string (e.g. 'broadsword', 'fire_infusion', 'health_potion').")]
        [SerializeField] private string id = "item_id";

        [Tooltip("Name displayed in the shop list and details panel.")]
        [SerializeField] private string displayName = "New Shop Item";

        [Tooltip("Classification: Weapons, Attribute, Consumables, or PhysicalUpgrade.")]
        [SerializeField] private ShopItemCategory category = ShopItemCategory.Weapons;

        [Tooltip("Character role compatibility: Both, Lumberjack, or Warrior.")]
        [SerializeField] private CharacterClass targetRole = CharacterClass.Both;

        [Tooltip("Detailed description of this item's effects or lore.")]
        [TextArea(3, 6)]
        [SerializeField] private string description = "Item description goes here.";

        [Tooltip("Visual icon displayed in the shop slot and details panel.")]
        [SerializeField] private Sprite icon;

        [Header("Combined Pricing (Wood + Coins BOTH Required)")]
        [Tooltip("Wood required to purchase. Both wood and coins must be sufficient.")]
        [Min(0)]
        [SerializeField] private int woodPrice = 50;

        [Tooltip("Coins/Gold required to purchase. Both wood and coins must be sufficient.")]
        [Min(0)]
        [SerializeField] private int coinPrice = 10;

        [Header("Modular Behavior Strategy")]
        [Tooltip("Pluggable behavior slot executed when purchased (Weapon Infusion, Stat Upgrade, Item Grant, etc.).")]
        [SerializeField] private ShopItemAction action;

        // --- Public Properties ---
        public string Id => id;
        public string DisplayName => displayName;
        public ShopItemCategory Category => category;
        public CharacterClass TargetRole => targetRole;
        public string Description => description;
        public Sprite Icon => icon;
        public int WoodPrice => woodPrice;
        public int CoinPrice => coinPrice;
        public ShopItemAction Action => action;
        public string EffectSummary => action != null ? action.EffectSummary : string.Empty;

        /// <summary>
        /// Editor / runtime helper to configure this definition.
        /// </summary>
        public void Configure(string newItemId, string newDisplayName, ShopItemCategory newCategory,
            CharacterClass newRole, string newDesc, Sprite newIcon, int newWoodPrice, int newCoinPrice,
            ShopItemAction newAction = null)
        {
            id = newItemId;
            displayName = newDisplayName;
            category = newCategory;
            targetRole = newRole;
            description = newDesc;
            icon = newIcon;
            woodPrice = newWoodPrice;
            coinPrice = newCoinPrice;
            action = newAction;
        }

        /// <summary>
        /// Validates if the customer's character class can purchase and use this item.
        /// </summary>
        public bool IsRoleCompatible(CharacterClass playerClass)
        {
            if (targetRole == CharacterClass.Both || playerClass == CharacterClass.Both) return true;
            return targetRole == playerClass;
        }

        /// <summary>
        /// Checks if a player has sufficient wood and coins to buy this item.
        /// Strict rule: BOTH currencies must be met simultaneously.
        /// </summary>
        public bool CanAfford(PlayerInventory buyer, out bool hasWood, out bool hasCoins)
        {
            hasWood = buyer != null && buyer.Wood >= woodPrice;
            hasCoins = buyer != null && buyer.Money >= coinPrice;
            return hasWood && hasCoins;
        }

        /// <summary>
        /// Formats role name with human-readable styling.
        /// </summary>
        public string GetRoleDisplayText()
        {
            return targetRole switch
            {
                CharacterClass.Warrior => "Warrior",
                CharacterClass.Lumberjack => "Lumberjack",
                _ => "All Classes"
            };
        }

        /// <summary>
        /// Formats role badge color.
        /// </summary>
        public Color GetRoleDisplayColor()
        {
            return targetRole switch
            {
                CharacterClass.Warrior => new Color(1f, 0.35f, 0.35f, 1f),     // Red / Orange
                CharacterClass.Lumberjack => new Color(1f, 0.72f, 0.15f, 1f),   // Gold / Amber
                _ => new Color(0.35f, 0.95f, 0.55f, 1f)                        // Green / Cyan
            };
        }
    }
}
