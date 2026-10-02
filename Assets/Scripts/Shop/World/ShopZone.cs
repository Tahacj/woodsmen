using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Woodsmen.Combat.Weapons;
using Woodsmen.Environment;
using Woodsmen.Inventory;
using Woodsmen.Shop.UI;
using Woodsmen.UI;

namespace Woodsmen.Shop
{
    /// <summary>
    /// World-space shop instance placed on tents or outpost buildings with a Trigger Box Collider.
    /// Implements IShop so multiple independent shops across the map (Blacksmith, Trader, Alchemist)
    /// seamlessly share the same professional infrastructure, interaction prompt, and UI.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class ShopZone : MonoBehaviour, IShop
    {
        [Header("Shop Identity")]
        [Tooltip("Display name shown in shop window header / logs.")]
        [SerializeField] private string shopName = "Trader's Outpost";

        [Tooltip("Prompt displayed on the Interaction Canvas when the player steps into range.")]
        [SerializeField] private string interactionPrompt = "[E] Open Shop";

        [Header("Items For Sale")]
        [Tooltip("List of items sold by this specific shop instance.")]
        [SerializeField] private List<ShopItemDefinition> itemsForSale = new List<ShopItemDefinition>();

        [Header("Interaction Settings")]
        [Tooltip("Optional reference point for player proximity. Defaults to this transform.")]
        [SerializeField] private Transform interactionCenter;

        [Tooltip("Fallback detection radius if physics trigger is not used.")]
        [SerializeField] private float fallbackInteractionRadius = 4.5f;

        [Tooltip("If the shop has a gate (door), assign the NetworkSlidingGate here. " +
                 "The shop UI will only open when the gate is already open, preventing E from firing both simultaneously.")]
        [SerializeField] private NetworkSlidingGate linkedGate;

        [Header("Audio Feedback (Optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openShopSound;
        [SerializeField] private AudioClip closeShopSound;

        // --- IShop Properties ---
        public string ShopName => shopName;
        public Transform Transform => transform;
        public IReadOnlyList<ShopItemDefinition> ItemsForSale => itemsForSale;

        /// <summary>True while the local player is inside this shop's proximity zone.</summary>
        public bool IsPlayerInside => _isPlayerInsideTrigger;

        private bool _isPlayerInsideTrigger = false;
        private PlayerInventory _currentCustomer = null;

        private void Reset()
        {
            // Auto-configure the trigger collider if added
            var colliders = GetComponents<Collider>();
            foreach (var col in colliders)
            {
                if (col.isTrigger) return;
            }
            // If no trigger exists, add a trigger BoxCollider
            var trigger = gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(5f, 2.5f, 5f);
        }

        private void Awake()
        {
            if (interactionCenter == null) interactionCenter = transform;
            if (audioSource == null) audioSource = GetComponent<AudioSource>();

            // Remove any missing or null item references
            itemsForSale.RemoveAll(item => item == null);

            // If empty, auto-populate from Resources/ShopItems
            if (itemsForSale.Count == 0)
            {
                var loaded = Resources.LoadAll<ShopItemDefinition>("ShopItems");
                if (loaded != null && loaded.Length > 0)
                {
                    itemsForSale.AddRange(loaded);
                }
            }
        }

        private void Update()
        {
            // Fallback proximity check if trigger didn't catch (e.g., player spawned inside)
            UpdateProximityDetection();

            if (_isPlayerInsideTrigger)
            {
                // Dynamically refresh the interaction prompt based on gate state
                if (!ShopUI.IsOpen)
                {
                    bool gateIsOpen = linkedGate == null || linkedGate.IsOpen;
                    if (gateIsOpen)
                        InteractionPromptUI.Instance?.Show(interactionPrompt);
                    // When gate is closed the gate script shows its own "[E] Open Gate" prompt
                }

                if (WasOpenKeyPressed())
                {
                    // If a gate is linked, only open the shop UI when the gate is already open.
                    // When the gate is closed, the NetworkSlidingGate script handles [E] to open it.
                    if (linkedGate == null || linkedGate.IsOpen)
                    {
                        ToggleShop();
                    }
                }
            }
        }

        private void UpdateProximityDetection()
        {
            var localPlayer = PlayerInventory.LocalPlayerInstance;
            if (localPlayer == null) return;

            Vector3 center = interactionCenter != null ? interactionCenter.position : transform.position;
            float distSqr = (localPlayer.transform.position - center).sqrMagnitude;
            float maxDistSqr = fallbackInteractionRadius * fallbackInteractionRadius;

            if (distSqr <= maxDistSqr)
            {
                if (!_isPlayerInsideTrigger)
                {
                    _currentCustomer = localPlayer;
                    OnLocalPlayerEntered();
                }
            }
            else
            {
                if (_isPlayerInsideTrigger)
                {
                    OnLocalPlayerExited();
                }
            }
        }

        #region Physics Trigger Detection

        private void OnTriggerEnter(Collider other)
        {
            var inv = GetPlayerInventory(other);
            if (inv != null && IsLocalPlayer(inv))
            {
                _currentCustomer = inv;
                OnLocalPlayerEntered();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            var inv = GetPlayerInventory(other);
            if (inv != null && IsLocalPlayer(inv))
            {
                OnLocalPlayerExited();
            }
        }

        private void OnLocalPlayerEntered()
        {
            _isPlayerInsideTrigger = true;

            if (_currentCustomer == null)
            {
                _currentCustomer = PlayerInventory.LocalPlayerInstance;
            }

            // Only show the "[E] Open Shop" prompt if the gate is open (or there is no gate).
            // If the gate is closed, the gate's own script will show "[E] Open Gate" first.
            if (!ShopUI.IsOpen)
            {
                bool gateIsOpen = linkedGate == null || linkedGate.IsOpen;
                if (gateIsOpen)
                {
                    InteractionPromptUI.Instance?.Show(interactionPrompt);
                }
            }
        }

        private void OnLocalPlayerExited()
        {
            _isPlayerInsideTrigger = false;
            _currentCustomer = null;

            InteractionPromptUI.Instance?.Hide();

            if (ShopUI.IsOpen && ShopUI.CurrentShop == (IShop)this)
            {
                ShopUI.Instance?.CloseShop();
            }
        }

        private PlayerInventory GetPlayerInventory(Collider other)
        {
            if (other == null) return null;
            var inv = other.GetComponentInParent<PlayerInventory>();
            if (inv != null) return inv;
            return other.GetComponent<PlayerInventory>();
        }

        private bool IsLocalPlayer(PlayerInventory inv)
        {
            if (inv == null) return false;
            if (PlayerInventory.LocalPlayerInstance != null)
            {
                return inv == PlayerInventory.LocalPlayerInstance;
            }
            if (inv.netIdentity != null)
            {
                return inv.netIdentity.isLocalPlayer;
            }
            return true;
        }

        #endregion

        #region Input Handling

        private bool WasOpenKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
            try
            {
                if (Input.GetKeyDown(KeyCode.E))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        private int _lastToggleFrame = -1;

        private void ToggleShop()
        {
            if (Time.frameCount <= _lastToggleFrame) return;
            _lastToggleFrame = Time.frameCount;

            if (ShopUI.IsOpen)
            {
                if (ShopUI.CurrentShop == (IShop)this)
                {
                    ShopUI.Instance?.CloseShop();
                }
            }
            else
            {
                OpenThisShop();
            }
        }

        public void OpenThisShop()
        {
            if (ShopUI.Instance == null)
            {
                Debug.LogError("<color=red>[ShopZone]</color> ShopUI.Instance is null! Make sure the Shop Canvas has the ShopUI component attached.");
                return;
            }

            var customer = _currentCustomer != null ? _currentCustomer : PlayerInventory.LocalPlayerInstance;
            InteractionPromptUI.Instance?.Hide();
            ShopUI.Instance.OpenShop(this, customer);
        }

        #endregion

        #region IShop Implementation

        public bool CanPurchase(PlayerInventory buyer, ShopItemDefinition item, out string failureReason)
        {
            failureReason = string.Empty;
            if (buyer == null)
            {
                failureReason = "No customer found.";
                return false;
            }

            if (item == null)
            {
                failureReason = "Item not found.";
                return false;
            }

            // 1. Role validation
            if (!item.IsRoleCompatible(buyer.CharacterClass))
            {
                failureReason = $"Class restricted! Only for {item.GetRoleDisplayText()}.";
                return false;
            }

            // 2. Dual currency validation (wood + coins combined)
            if (!item.CanAfford(buyer, out bool hasWood, out bool hasCoins))
            {
                if (!hasWood && !hasCoins)
                {
                    failureReason = $"Insufficient resources! Need {item.WoodPrice} Wood and {item.CoinPrice} Coins.";
                }
                else if (!hasWood)
                {
                    failureReason = $"Not enough wood! Need {item.WoodPrice} Wood (have {buyer.Wood}).";
                }
                else
                {
                    failureReason = $"Not enough coins! Need {item.CoinPrice} Coins (have {buyer.Money}).";
                }
                return false;
            }

            // 3. Inventory capacity validation
            if (!HasInventorySpace(buyer, item))
            {
                failureReason = "Inventory is full!";
                return false;
            }

            // 4. One-time purchase check for attributes and physical upgrades
            if (IsOneTimePurchaseAlreadyOwned(buyer, item))
            {
                failureReason = "Already purchased!";
                return false;
            }

            return true;
        }

        public static bool IsOneTimePurchaseAlreadyOwned(PlayerInventory buyer, ShopItemDefinition item)
        {
            if (buyer == null || item == null) return false;

            // Physical Upgrades are no longer one-time purchases (stackable infinitely)
            if (item.Category != ShopItemCategory.Attribute)
            {
                return false;
            }

            var upgrades = buyer.GetComponentInParent<PlayerUpgrades>() ??
                           buyer.GetComponentInChildren<PlayerUpgrades>() ??
                           buyer.GetComponent<PlayerUpgrades>();

            if (upgrades != null && upgrades.HasUpgrade(item.Id))
            {
                return true;
            }

            if (item.Action is Woodsmen.Shop.Actions.WeaponInfusionAction infusionAction)
            {
                var infusion = buyer.GetComponentInParent<WeaponInfusion>() ??
                               buyer.GetComponentInChildren<WeaponInfusion>() ??
                               buyer.GetComponent<WeaponInfusion>();

                if (infusion != null && infusion.CurrentType == infusionAction.InfusionType && infusion.CurrentType != InfusionType.None)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasInventorySpace(PlayerInventory buyer, ShopItemDefinition item)
        {
            if (buyer == null || item == null) return false;

            // Attributes and Physical Upgrades do not consume inventory bag slots
            if (item.Category == ShopItemCategory.Attribute || item.Category == ShopItemCategory.PhysicalUpgrade)
            {
                return true;
            }

            string targetItemId = item.Id;
            if (item.Action is Woodsmen.Shop.Actions.GrantInventoryItemAction grantAction && grantAction.InventoryItem != null)
            {
                targetItemId = grantAction.InventoryItem.Id;
            }

            foreach (var slot in buyer.Slots)
            {
                if (slot.IsEmpty) return true;
                if (string.Equals(slot.itemId, targetItemId, StringComparison.OrdinalIgnoreCase))
                {
                    var def = ItemDatabase.GetItem(targetItemId);
                    int maxStack = def != null ? def.MaxStackSize : 1;
                    if (slot.quantity < maxStack) return true;
                }
            }
            return false;
        }

        public bool PurchaseItem(PlayerInventory buyer, ShopItemDefinition item, out string resultMessage)
        {
            resultMessage = string.Empty;
            if (!CanPurchase(buyer, item, out string reason))
            {
                resultMessage = reason;
                return false;
            }

            // Deduct combined currencies (Wood + Coins)
            bool woodRemoved = buyer.RemoveWood(item.WoodPrice);
            bool coinsRemoved = buyer.RemoveMoney(item.CoinPrice);

            if (!woodRemoved && item.WoodPrice > 0)
            {
                resultMessage = "Failed to deduct wood.";
                return false;
            }

            if (!coinsRemoved && item.CoinPrice > 0)
            {
                resultMessage = "Failed to deduct coins.";
                return false;
            }

            // Grant item into inventory + apply upgrade payload
            GrantPurchasedItem(buyer, item);

            resultMessage = $"Successfully bought {item.DisplayName}!";
            Debug.Log($"<color=#10b981><b>[Shop]</b> Transaction Complete! Customer bought '{item.DisplayName}' for {item.WoodPrice} wood + {item.CoinPrice} coins.</color>");
            return true;
        }

        private void GrantPurchasedItem(PlayerInventory buyer, ShopItemDefinition item)
        {
            if (buyer == null || item == null) return;

            // 1. If modular behavior strategy is assigned, execute it
            if (item.Action != null)
            {
                item.Action.Execute(buyer, item);
                return;
            }

            // 2. Fallback for basic shop items without dedicated actions: register and grant into inventory
            string targetItemId = item.Id;
            IInventoryItem invItem = ItemDatabase.GetItem(targetItemId);

            if (invItem == null || invItem.DisplayName == targetItemId || invItem.Icon == null)
            {
                if (item.Category == ShopItemCategory.Consumables)
                {
                    invItem = ConsumableItemDefinition.CreateRuntimeConsumable(
                        id: targetItemId,
                        displayName: item.DisplayName,
                        healthRestore: 50f,
                        maxStackSize: 20,
                        icon: item.Icon,
                        allowedClass: item.TargetRole
                    );
                }
                else
                {
                    ItemCategory cat = item.Category switch
                    {
                        ShopItemCategory.Weapons => ItemCategory.Weapon,
                        ShopItemCategory.Consumables => ItemCategory.Consumable,
                        _ => ItemCategory.Misc
                    };

                    invItem = ItemDefinition.CreateRuntimeInstance(
                        id: targetItemId,
                        displayName: item.DisplayName,
                        category: cat,
                        maxStackSize: item.Category == ShopItemCategory.Consumables ? 20 : 1,
                        isUsable: item.Category == ShopItemCategory.Consumables,
                        icon: item.Icon,
                        allowedClass: item.TargetRole
                    );
                }
                ItemDatabase.RegisterItem(invItem);
            }

            bool added = buyer.AddItem(invItem.Id, 1);
            if (added)
            {
                Debug.Log($"<color=#10b981><b>[Shop]</b> Added 1x '{invItem.DisplayName}' to {buyer.name}'s inventory.</color>");
            }
            else
            {
                Debug.LogWarning($"<color=#f59e0b><b>[Shop]</b> Could not add '{invItem.DisplayName}' to inventory (Full).</color>");
            }
        }

        public void OnShopOpened(PlayerInventory customer)
        {
            InteractionPromptUI.Instance?.Hide();
            if (audioSource != null && openShopSound != null)
            {
                audioSource.PlayOneShot(openShopSound);
            }
        }

        public void OnShopClosed(PlayerInventory customer)
        {
            if (audioSource != null && closeShopSound != null)
            {
                audioSource.PlayOneShot(closeShopSound);
            }

            // Re-show interaction prompt if player is still in the trigger zone
            if (_isPlayerInsideTrigger)
            {
                InteractionPromptUI.Instance?.Show(interactionPrompt);
            }
        }

        #endregion

        #region Public Configuration

        /// <summary>
        /// Adds an item to this shop's inventory at runtime.
        /// </summary>
        public void AddItem(ShopItemDefinition item)
        {
            if (item != null && !itemsForSale.Contains(item))
            {
                itemsForSale.Add(item);
            }
        }

        /// <summary>
        /// Clears all items currently in this shop.
        /// </summary>
        public void ClearItems()
        {
            itemsForSale.Clear();
        }

        #endregion

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.4f, 0.35f);
            Vector3 center = interactionCenter != null ? interactionCenter.position : transform.position;
            Gizmos.DrawWireSphere(center, fallbackInteractionRadius);
        }
    }
}
