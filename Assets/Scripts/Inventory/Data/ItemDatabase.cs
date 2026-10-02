using System;
using System.Collections.Generic;
using UnityEngine;

namespace Woodsmen.Inventory
{
    /// <summary>
    /// Central registry and catalog for all items implementing IInventoryItem.
    /// Supports polymorphic item definitions loaded from Resources/Items or registered dynamically.
    /// Automatically provisions default items (Wood, Stone, Health Potion) with zero manual setup.
    /// </summary>
    public static class ItemDatabase
    {
        private static readonly Dictionary<string, IInventoryItem> _itemsById =
            new Dictionary<string, IInventoryItem>(StringComparer.OrdinalIgnoreCase);

        private static bool _isInitialized;

        public static IReadOnlyCollection<IInventoryItem> AllItems
        {
            get
            {
                EnsureInitialized();
                return _itemsById.Values;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeOnLoad()
        {
            EnsureInitialized();
        }

        public static void EnsureInitialized()
        {
            if (_isInitialized) return;

            _itemsById.Clear();

            // 1. Scan Resources/Items for author-created ScriptableObjects implementing IInventoryItem
            ItemDefinition[] loadedItems = Resources.LoadAll<ItemDefinition>("Items");
            if (loadedItems != null)
            {
                foreach (var item in loadedItems)
                {
                    if (item != null && !string.IsNullOrEmpty(item.Id))
                    {
                        _itemsById[item.Id] = item;
                    }
                }
            }

            // 2. Register essential default items if not already provided
            RegisterDefaultFallbacks();

            _isInitialized = true;
        }

        public static IInventoryItem GetItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            EnsureInitialized();

            if (_itemsById.TryGetValue(id, out IInventoryItem item))
            {
                return item;
            }

            Debug.LogWarning($"[ItemDatabase] Item with id '{id}' not found! Auto-generating fallback.");
            var fallback = ItemDefinition.CreateRuntimeInstance(id, id, ItemCategory.Misc);
            _itemsById[id] = fallback;
            return fallback;
        }

        public static bool RegisterItem(IInventoryItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id)) return false;
            EnsureInitialized();
            _itemsById[item.Id] = item;
            return true;
        }

        private static void RegisterDefaultFallbacks()
        {
            // --- 1. Wood (The Core Harvestable Resource - Both Classes) ---
            if (!_itemsById.ContainsKey("wood"))
            {
                Sprite woodSprite = Resources.Load<Sprite>("Items/wood/wood");
                if (woodSprite == null) woodSprite = UI.UIStyleHelper.GetItemIcon("wood");

                _itemsById["wood"] = ItemDefinition.CreateRuntimeInstance(
                    id: "wood",
                    displayName: "Wood Log",
                    category: ItemCategory.Resource,
                    maxStackSize: 999,
                    isUsable: false,
                    icon: woodSprite,
                    allowedClass: CharacterClass.Both
                );
            }

            // --- 2. Coin (Universal Gold Currency Resource - Both Classes) ---
            if (!_itemsById.ContainsKey("coin"))
            {
                Sprite coinSprite = Resources.Load<Sprite>("Coin/source/Coin png") ??
                                    Resources.Load<Sprite>("Coin/source/stack-of-coins-png");
                if (coinSprite == null) coinSprite = UI.UIStyleHelper.GetItemIcon("coin");

                var coinItem = ItemDefinition.CreateRuntimeInstance(
                    id: "coin",
                    displayName: "Gold Coin",
                    category: ItemCategory.Currency,
                    maxStackSize: 999,
                    isUsable: false,
                    icon: coinSprite,
                    allowedClass: CharacterClass.Both
                );

                _itemsById["coin"] = coinItem;
                _itemsById["money"] = coinItem;
            }

            // --- 2. Health Potion (Consumable - Both Classes) ---
            if (!_itemsById.ContainsKey("health_potion"))
            {
                _itemsById["health_potion"] = ConsumableItemDefinition.CreateRuntimeConsumable(
                    id: "health_potion",
                    displayName: "Herbal Healing Salve",
                    healthRestore: 50f,
                    maxStackSize: 20,
                    allowedClass: CharacterClass.Both
                );
            }

            // --- 3. Lumberjack Only: Woodcutter Stew ---
            if (!_itemsById.ContainsKey("woodcutter_stew"))
            {
                _itemsById["woodcutter_stew"] = ConsumableItemDefinition.CreateRuntimeConsumable(
                    id: "woodcutter_stew",
                    displayName: "Hearty Woodcutter Stew",
                    healthRestore: 75f,
                    maxStackSize: 10,
                    allowedClass: CharacterClass.Lumberjack
                );
            }

            // --- 4. Warrior Only: Battle Draught ---
            if (!_itemsById.ContainsKey("warrior_draught"))
            {
                _itemsById["warrior_draught"] = ConsumableItemDefinition.CreateRuntimeConsumable(
                    id: "warrior_draught",
                    displayName: "Ironclad Battle Draught",
                    healthRestore: 100f,
                    maxStackSize: 5,
                    allowedClass: CharacterClass.Warrior
                );
            }
        }
    }
}
