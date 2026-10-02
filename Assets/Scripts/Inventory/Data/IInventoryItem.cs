using UnityEngine;

namespace Woodsmen.Inventory
{
    /// <summary>
    /// Contract for all inventory items in the game.
    /// Enables polymorphic item behavior where different item types (Resources, Consumables, Tools, Equipment)
    /// share the same core contract but execute specialized logic when used.
    /// </summary>
    public interface IInventoryItem
    {
        /// <summary>Unique string identifier (e.g. 'wood', 'stone', 'health_potion').</summary>
        string Id { get; }

        /// <summary>Display name formatted for UI presentation.</summary>
        string DisplayName { get; }

        /// <summary>Visual icon sprite displayed in inventory slots and inspector.</summary>
        Sprite Icon { get; }

        /// <summary>Classification category (Resource, Tool, Weapon, Consumable, etc.).</summary>
        ItemCategory Category { get; }

        /// <summary>Maximum quantity allowed in a single slot stack.</summary>
        int MaxStackSize { get; }

        /// <summary>The physical 3D prefab spawned in the world when this item is dropped.</summary>
        GameObject WorldDropPrefab { get; }

        /// <summary>
        /// Class restriction for this item: Both (Universal), Lumberjack only, or Warrior only.
        /// </summary>
        CharacterClass AllowedClass { get; }

        /// <summary>
        /// Whether this item can be used or activated from the inventory (e.g. consumables vs passive resources).
        /// </summary>
        bool IsUsable { get; }

        /// <summary>
        /// Whether this item is permitted to be dropped into the world.
        /// Strictly enforced: Weapons, Attributes, and Physical Upgrades cannot be dropped.
        /// Only consumables (and universal resources) designated for Both classes can be dropped.
        /// </summary>
        bool IsDroppable { get; }

        /// <summary>
        /// Evaluates whether this item can currently be used or activated by the specified character.
        /// </summary>
        /// <param name="user">The GameObject of the player/character attempting to use the item.</param>
        /// <returns>True if the character meets conditions to use this item.</returns>
        bool CanUse(GameObject user);

        /// <summary>
        /// Executes the item's unique behavior polymorphically (e.g. healing, buffing, equipping).
        /// </summary>
        /// <param name="user">The GameObject of the player/character using the item.</param>
        /// <returns>True if the item was successfully consumed/used (stack should decrement by 1).</returns>
        bool Use(GameObject user);
    }
}
