using System;
using UnityEngine;

namespace Woodsmen.Inventory
{
    /// <summary>
    /// Represents an individual slot in the character's inventory holding a specific item stack.
    /// Serialized by Unity and Mirror for network replication and save games.
    /// Works with polymorphic IInventoryItem definitions.
    /// </summary>
    [Serializable]
    public struct InventorySlot : IEquatable<InventorySlot>
    {
        [Tooltip("The unique identifier matching an IInventoryItem (e.g. 'wood', 'stone', 'health_potion').")]
        public string itemId;

        [Tooltip("Number of items held in this stack.")]
        public int quantity;

        public InventorySlot(string itemId, int quantity)
        {
            this.itemId = itemId ?? string.Empty;
            this.quantity = Mathf.Max(0, quantity);
            if (this.quantity == 0)
            {
                this.itemId = string.Empty;
            }
        }

        public static InventorySlot Empty => new InventorySlot(string.Empty, 0);

        public bool IsEmpty => string.IsNullOrEmpty(itemId) || quantity <= 0;

        /// <summary>
        /// Retrieves the polymorphic item contract from the central registry.
        /// </summary>
        public IInventoryItem Item => !IsEmpty ? ItemDatabase.GetItem(itemId) : null;

        /// <summary>
        /// True if this slot can accept more units of the specified IInventoryItem.
        /// </summary>
        public bool CanStack(IInventoryItem item, out int spaceRemaining)
        {
            spaceRemaining = 0;
            if (item == null) return false;

            if (IsEmpty)
            {
                spaceRemaining = item.MaxStackSize;
                return true;
            }

            if (string.Equals(itemId, item.Id, StringComparison.OrdinalIgnoreCase))
            {
                spaceRemaining = Mathf.Max(0, item.MaxStackSize - quantity);
                return spaceRemaining > 0;
            }

            return false;
        }

        /// <summary>
        /// Adds a quantity up to maxStackSize. Returns unconsumed surplus.
        /// </summary>
        public int Add(int amount, int maxStackSize)
        {
            if (amount <= 0) return 0;

            int capacity = Mathf.Max(0, maxStackSize - quantity);
            int toAdd = Mathf.Min(capacity, amount);
            quantity += toAdd;

            return amount - toAdd;
        }

        /// <summary>
        /// Removes a quantity from this slot. Returns actual quantity removed.
        /// </summary>
        public int Remove(int amount)
        {
            if (amount <= 0 || IsEmpty) return 0;

            int removed = Mathf.Min(quantity, amount);
            quantity -= removed;

            if (quantity <= 0)
            {
                itemId = string.Empty;
                quantity = 0;
            }

            return removed;
        }

        public void Clear()
        {
            itemId = string.Empty;
            quantity = 0;
        }

        public bool Equals(InventorySlot other)
        {
            return string.Equals(itemId, other.itemId, StringComparison.OrdinalIgnoreCase) && quantity == other.quantity;
        }

        public override bool Equals(object obj)
        {
            return obj is InventorySlot other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(itemId, quantity);
        }

        public override string ToString()
        {
            return IsEmpty ? "[Empty Slot]" : $"[{itemId} x{quantity}]";
        }
    }
}
