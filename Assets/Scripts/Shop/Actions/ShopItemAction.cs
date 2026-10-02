using UnityEngine;
using Woodsmen.Inventory;

namespace Woodsmen.Shop
{
    /// <summary>
    /// Abstract Strategy for custom shop item behaviors.
    /// Replaces hardcoded fields in ShopItemDefinition with modular, polymorphic behaviors
    /// (e.g. Weapon Infusions, Stat Upgrades, Inventory Grants, Custom Spells).
    /// </summary>
    public abstract class ShopItemAction : ScriptableObject
    {
        /// <summary>Summary displayed in the details panel under the description.</summary>
        public abstract string EffectSummary { get; }

        /// <summary>Executes the custom gameplay effect when this item is bought.</summary>
        public abstract void Execute(PlayerInventory buyer, ShopItemDefinition item);
    }
}
