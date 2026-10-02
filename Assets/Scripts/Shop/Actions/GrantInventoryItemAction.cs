using UnityEngine;
using Woodsmen.Inventory;

namespace Woodsmen.Shop.Actions
{
    /// <summary>
    /// Modular shop action for physical items (Weapons, Tools, Consumables).
    /// Places the purchased item directly into the player's inventory slots.
    /// </summary>
    [CreateAssetMenu(fileName = "NewGrantInventoryItemAction", menuName = "Woodsmen/Shop/Actions/Grant Inventory Item Action", order = 32)]
    public class GrantInventoryItemAction : ShopItemAction
    {
        [Header("Item To Grant")]
        [SerializeField] private ItemDefinition inventoryItem;
        [SerializeField] private int quantity = 1;

        [Header("Summary Display")]
        [SerializeField] private string summary = string.Empty;

        public ItemDefinition InventoryItem => inventoryItem;
        public int Quantity => quantity;

        public override string EffectSummary
        {
            get
            {
                if (!string.IsNullOrEmpty(summary)) return summary;
                return inventoryItem != null ? $"Adds 1x {inventoryItem.DisplayName} to Inventory" : string.Empty;
            }
        }

        public override void Execute(PlayerInventory buyer, ShopItemDefinition item)
        {
            if (buyer == null) return;

            string itemId = inventoryItem != null ? inventoryItem.Id : item.Id;

            // Ensure item is registered in ItemDatabase
            IInventoryItem registered = ItemDatabase.GetItem(itemId);
            if (registered == null || registered.DisplayName == itemId || registered.Icon == null)
            {
                if (item.Category == ShopItemCategory.Consumables)
                {
                    registered = ConsumableItemDefinition.CreateRuntimeConsumable(
                        id: itemId,
                        displayName: item.DisplayName,
                        healthRestore: 50f,
                        maxStackSize: 20,
                        icon: item.Icon,
                        allowedClass: item.TargetRole
                    );
                }
                else
                {
                    registered = ItemDefinition.CreateRuntimeInstance(
                        id: itemId,
                        displayName: item.DisplayName,
                        category: ItemCategory.Weapon,
                        maxStackSize: 1,
                        isUsable: false,
                        icon: item.Icon,
                        allowedClass: item.TargetRole
                    );
                }
                ItemDatabase.RegisterItem(registered);
            }

            buyer.AddItem(registered.Id, Mathf.Max(1, quantity));
            Debug.Log($"<color=#10b981><b>[ShopAction]</b> Granted {quantity}x {item.DisplayName} to {buyer.name}'s inventory.</color>");
        }

        public void Configure(ItemDefinition item, int count, string displaySummary)
        {
            inventoryItem = item;
            quantity = count;
            summary = displaySummary;
        }
    }
}
