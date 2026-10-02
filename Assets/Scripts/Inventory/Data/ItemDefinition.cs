using System;
using UnityEngine;
using Woodsmen.Combat;

namespace Woodsmen.Inventory
{
    /// <summary>
    /// Base ScriptableObject implementing IInventoryItem.
    /// Provides standard configuration for items and virtual hooks for polymorphic usage behaviors.
    /// </summary>
    [CreateAssetMenu(fileName = "NewItem", menuName = "Woodsmen/Inventory/Base Item Definition", order = 10)]
    public class ItemDefinition : ScriptableObject, IInventoryItem
    {
        [Header("Identity")]
        [Tooltip("Unique string identifier (e.g. 'wood', 'stone', 'health_potion').")]
        [SerializeField] private string id = "item_id";

        [Tooltip("Human-friendly name displayed in UI.")]
        [SerializeField] private string displayName = "New Item";

        [Tooltip("Visual icon displayed in inventory slots.")]
        [SerializeField] private Sprite icon;

        [Header("Classification & Stacking")]
        [Tooltip("Category of the item.")]
        [SerializeField] private ItemCategory category = ItemCategory.Resource;

        [Tooltip("Maximum units that can be stacked in a single inventory slot.")]
        [Min(1)]
        [SerializeField] private int maxStackSize = 999;

        [Header("Usage & Class Restriction")]
        [Tooltip("Can this item be consumed or activated from the inventory?")]
        [SerializeField] private bool isUsable = false;

        [Tooltip("Which character class can use this item: Both (Universal), Lumberjack, or Warrior.")]
        [SerializeField] private CharacterClass allowedClass = CharacterClass.Both;

        [Header("Drop Settings")]
        [Tooltip("Explicit override for whether this item can be dropped. If false, cannot be dropped under any circumstances.")]
        [SerializeField] private bool allowDrop = true;

        [Tooltip("The physical 3D prefab spawned in the world when this item is dropped from the inventory.")]
        [SerializeField] private GameObject worldDropPrefab;

        // --- IInventoryItem Implementation ---
        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon
        {
            get => icon;
            set => icon = value;
        }
        public ItemCategory Category => category;
        public int MaxStackSize => maxStackSize;
        public virtual bool IsUsable => isUsable;
        public GameObject WorldDropPrefab => worldDropPrefab;
        public CharacterClass AllowedClass
        {
            get => allowedClass;
            set => allowedClass = value;
        }

        /// <summary>
        /// Strictly enforced drop rules:
        /// - Weapons, Armor, Tools, Attributes, and Physical Upgrades (Misc) are NOT droppable.
        /// - Consumables are droppable for everyone (any class restriction).
        /// - Resources and Currencies are droppable.
        /// </summary>
        public virtual bool IsDroppable
        {
            get
            {
                if (!allowDrop) return false;

                // Weapons, tools, and armor are never droppable
                if (category == ItemCategory.Weapon || category == ItemCategory.Tool || category == ItemCategory.Armor)
                    return false;

                // Attributes & Physical Upgrades (Misc) are never droppable
                if (category == ItemCategory.Misc)
                    return false;

                // Consumables (potions, food) are always droppable
                if (category == ItemCategory.Consumable)
                    return true;

                // Universal resources and currencies (Wood, Coin) are droppable
                if (category == ItemCategory.Resource || category == ItemCategory.Currency)
                    return allowedClass == CharacterClass.Both;

                return false;
            }
        }

        /// <summary>
        /// Validates whether the given user matches the class restrictions of this item.
        /// </summary>
        public bool IsClassAllowed(GameObject user)
        {
            if (allowedClass == CharacterClass.Both) return true;
            if (user == null) return true;

            CharacterClass userClass = user.GetCharacterClass();
            return allowedClass.IsClassCompatible(userClass);
        }

        /// <summary>
        /// Virtual check: determines whether the specified user can use this item.
        /// </summary>
        public virtual bool CanUse(GameObject user)
        {
            if (!isUsable) return false;
            if (user != null && !IsClassAllowed(user)) return false;
            return true;
        }

        /// <summary>
        /// Virtual execution: performs the item's unique behavior polymorphically.
        /// Returns true if consumed (decrementing stack).
        /// </summary>
        public virtual bool Use(GameObject user)
        {
            if (!CanUse(user)) return false;
            return false;
        }

        /// <summary>
        /// Protected initializer for runtime instances and subclasses.
        /// </summary>
        protected void InitializeItem(
            string id,
            string displayName,
            ItemCategory category,
            int maxStackSize,
            bool isUsable,
            Sprite icon,
            CharacterClass allowedClass)
        {
            this.id = id;
            this.displayName = displayName;
            this.category = category;
            this.maxStackSize = Mathf.Max(1, maxStackSize);
            this.isUsable = isUsable;
            this.icon = icon;
            this.allowedClass = allowedClass;
        }

        /// <summary>
        /// Factory helper for creating runtime item instances.
        /// </summary>
        public static ItemDefinition CreateRuntimeInstance(
            string id,
            string displayName,
            ItemCategory category,
            int maxStackSize = 999,
            bool isUsable = false,
            Sprite icon = null,
            CharacterClass allowedClass = CharacterClass.Both)
        {
            var def = CreateInstance<ItemDefinition>();
            def.InitializeItem(id, displayName, category, maxStackSize, isUsable, icon, allowedClass);
            return def;
        }
    }
}
