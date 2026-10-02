using System;
using System.Collections.Generic;

namespace Woodsmen.Inventory
{
    /// <summary>
    /// Read-only contract for UI, HUD, and inspector systems.
    /// Exposes item slots, counts, and change events without allowing external state mutation.
    /// </summary>
    public interface IReadOnlyInventory
    {
        /// <summary>All item slots currently available in the inventory.</summary>
        IReadOnlyList<InventorySlot> Slots { get; }

        /// <summary>Total number of inventory slots.</summary>
        int SlotCount { get; }

        /// <summary>Current wood units in stock (for backward compatibility).</summary>
        int Wood { get; }

        /// <summary>Current coin currency in stock.</summary>
        int Money { get; }

        /// <summary>Retrieves the total quantity of a given item held across all slots.</summary>
        int GetItemCount(string itemId);

        /// <summary>True if the inventory contains at least the specified quantity of the item.</summary>
        bool HasItem(string itemId, int quantity = 1);

        /// <summary>Fired whenever any slot in the inventory changes (items added, removed, stacked, or consumed).</summary>
        event Action OnInventoryChanged;

        /// <summary>Fired when an item stack is added or incremented.</summary>
        event Action<InventorySlot> OnItemAdded;

        /// <summary>Fired when an item stack is removed or decremented.</summary>
        event Action<InventorySlot> OnItemRemoved;

        /// <summary>Fired when the wood count changes (for legacy HUD compatibility). Argument is new total.</summary>
        event Action<int> OnWoodChanged;

        /// <summary>Fired when the money count changes. Argument is new total.</summary>
        event Action<int> OnMoneyChanged;
    }

    /// <summary>
    /// Full universal inventory contract for player characters and containers.
    /// Provides operations for storing, stacking, removing, and polymorphically using items.
    /// </summary>
    public interface IInventory : IReadOnlyInventory
    {
        /// <summary>Adds a quantity of an item by id into the inventory.</summary>
        bool AddItem(string itemId, int quantity = 1);

        /// <summary>Adds a quantity of an item implementing IInventoryItem into the inventory.</summary>
        bool AddItem(IInventoryItem item, int quantity = 1);

        /// <summary>Removes a quantity of an item by id from the inventory.</summary>
        bool RemoveItem(string itemId, int quantity = 1);

        /// <summary>
        /// Consumes or activates an item at a specific inventory slot index.
        /// Executes the item's polymorphic Use() behavior.
        /// </summary>
        bool UseItem(int slotIndex);

        /// <summary>Adds a positive amount of wood (backward compatibility).</summary>
        void AddWood(int amount);

        /// <summary>Attempts to subtract an amount of wood (backward compatibility).</summary>
        bool RemoveWood(int amount);

        /// <summary>Adds a positive amount of money.</summary>
        void AddMoney(int amount);

        /// <summary>Attempts to subtract an amount of money.</summary>
        bool RemoveMoney(int amount);
    }
}
