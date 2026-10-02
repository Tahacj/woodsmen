using System.Collections.Generic;
using UnityEngine;
using Woodsmen.Inventory;

namespace Woodsmen.Shop
{
    /// <summary>
    /// Professional interface contract for all shops across the game map.
    /// Allows multiple shops (Blacksmith, Alchemist, General Outpost) to share the same
    /// unified UI, interaction prompt, and transaction logic.
    /// </summary>
    public interface IShop
    {
        /// <summary>Name / Header of this shop (e.g. "Camp Armory", "Forest Outpost").</summary>
        string ShopName { get; }

        /// <summary>Transform / World position of this shop.</summary>
        Transform Transform { get; }

        /// <summary>The inventory of items offered by this shop.</summary>
        IReadOnlyList<ShopItemDefinition> ItemsForSale { get; }

        /// <summary>
        /// Validates if the customer is eligible to purchase the selected item.
        /// Checks dual currency (wood + coins) and character class compatibility.
        /// </summary>
        bool CanPurchase(PlayerInventory buyer, ShopItemDefinition item, out string failureReason);

        /// <summary>
        /// Executes the transaction: deducts wood AND coins, then grants the item or upgrade.
        /// Returns true if transaction succeeded.
        /// </summary>
        bool PurchaseItem(PlayerInventory buyer, ShopItemDefinition item, out string resultMessage);

        /// <summary>Called when a customer opens the shop interface.</summary>
        void OnShopOpened(PlayerInventory customer);

        /// <summary>Called when a customer closes or walks away from the shop interface.</summary>
        void OnShopClosed(PlayerInventory customer);
    }
}
