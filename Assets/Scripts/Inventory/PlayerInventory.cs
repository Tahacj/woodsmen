using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Woodsmen.Environment;

namespace Woodsmen.Inventory
{
    /// <summary>
    /// Universal Character Inventory component.
    /// Features:
    /// - Slot-based storage with stack capacity limits and multi-item support.
    /// - Polymorphic item usage: executes IInventoryItem.Use() directly on the character.
    /// - Server-authoritative Mirror networking (SyncList) alongside 100% offline singleplayer fallback.
    /// - Event-driven callbacks for UI canvases, HUD counters, and game systems.
    /// </summary>
    public class PlayerInventory : NetworkBehaviour, IInventory, ICharacterClassProvider
    {
        public static PlayerInventory LocalPlayerInstance { get; private set; }

        [Header("Character Class / Role")]
        [Tooltip("Class for this character. If 'Both', auto-detects from GameObject name/components.")]
        [SerializeField] private CharacterClass characterClass = CharacterClass.Both;

        public CharacterClass CharacterClass
        {
            get
            {
                if (characterClass == CharacterClass.Both)
                {
                    characterClass = gameObject.GetCharacterClass();
                }
                return characterClass;
            }
            set => characterClass = value;
        }

        [Header("Inventory Capacity")]
        [Tooltip("Total number of storage slots.")]
        [SerializeField] private int maxSlots = 24;

        [Header("Starting Resources")]
        [Tooltip("Initial wood resource count given on spawn.")]
        [SerializeField] private int startingWood = 0;

        [Tooltip("Initial coin currency given on spawn.")]
        [SerializeField] private int startingMoney = 0;

        // --- Synchronized Network State ---
        private readonly SyncList<InventorySlot> _syncSlots = new SyncList<InventorySlot>();

        [SyncVar(hook = nameof(OnMoneySyncChanged))]
        private int _money;

        // Offline / Local fallback slot list
        private readonly List<InventorySlot> _localSlots = new List<InventorySlot>();

        public IReadOnlyList<InventorySlot> Slots
        {
            get
            {
                if ((NetworkClient.active || NetworkServer.active) && _syncSlots.Count > 0)
                {
                    return _syncSlots;
                }
                return _localSlots;
            }
        }

        public int SlotCount => Slots.Count;

        /// <summary>
        /// Total wood held in inventory (backward compatibility for HUD and pickups).
        /// </summary>
        public int Wood => GetItemCount("wood");

        /// <summary>
        /// Current gold coins held in inventory.
        /// </summary>
        public int Money
        {
            get
            {
                int coinCount = GetItemCount("coin");
                return coinCount > 0 ? coinCount : _money;
            }
        }

        // --- Event Hooks ---
        public event Action OnInventoryChanged;
        public event Action<InventorySlot> OnItemAdded;
        public event Action<InventorySlot> OnItemRemoved;
        public event Action<int> OnWoodChanged;
        public event Action<int> OnMoneyChanged;

        // --- Authority & Mode Helpers ---
        private bool IsOffline => !NetworkClient.active && !NetworkServer.active;
        private bool IsLocalAuthority => IsOffline || (netIdentity != null && netIdentity.isLocalPlayer);
        private bool IsServerActive => !IsOffline && netIdentity != null && netIdentity.isServer;
        private bool IsClientOnly => !IsOffline && netIdentity != null && netIdentity.isClient && !netIdentity.isServer;

        private void Awake()
        {
            _money = startingMoney;
            InitializeSlots();
        }

        private void Start()
        {
            if (IsLocalAuthority)
            {
                LocalPlayerInstance = this;

                // Grant starting wood if configured
                if (startingWood > 0 && Wood == 0)
                {
                    AddItem("wood", startingWood);
                }

                // Grant starting money/coins if configured
                if (startingMoney > 0 && Money == 0)
                {
                    AddItem("coin", startingMoney);
                }
                else if (_money > 0 && GetItemCount("coin") == 0)
                {
                    AddItem("coin", _money);
                }

                NotifyAllChanged();
            }
        }

        private void OnDestroy()
        {
            if (LocalPlayerInstance == this)
            {
                LocalPlayerInstance = null;
            }
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            LocalPlayerInstance = this;
            NotifyAllChanged();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_syncSlots.Count == 0)
            {
                for (int i = 0; i < maxSlots; i++)
                {
                    _syncSlots.Add(InventorySlot.Empty);
                }
            }

            if (startingWood > 0)
            {
                ServerAddItem("wood", startingWood);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _syncSlots.Callback += OnSyncSlotsChanged;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _syncSlots.Callback -= OnSyncSlotsChanged;
        }

        private void InitializeSlots()
        {
            if (_localSlots.Count == 0)
            {
                for (int i = 0; i < maxSlots; i++)
                {
                    _localSlots.Add(InventorySlot.Empty);
                }
            }
        }

        private int _lastNotifiedWood = -1;
        private int _lastNotifiedMoney = -1;

        private void OnSyncSlotsChanged(SyncList<InventorySlot>.Operation op, int itemIndex, InventorySlot oldItem, InventorySlot newItem)
        {
            NotifySlotChanges();
        }

        private void OnMoneySyncChanged(int oldVal, int newVal)
        {
            if (newVal != _lastNotifiedMoney)
            {
                _lastNotifiedMoney = newVal;
                OnMoneyChanged?.Invoke(newVal);
            }
            OnInventoryChanged?.Invoke();
        }

        private void NotifySlotChanges()
        {
            int currentWood = Wood;
            if (currentWood != _lastNotifiedWood)
            {
                _lastNotifiedWood = currentWood;
                OnWoodChanged?.Invoke(currentWood);
            }

            int currentMoney = Money;
            if (currentMoney != _lastNotifiedMoney)
            {
                _lastNotifiedMoney = currentMoney;
                _money = currentMoney;
                OnMoneyChanged?.Invoke(currentMoney);
            }

            OnInventoryChanged?.Invoke();
        }

        private void NotifyMoneyChanged()
        {
            if (_money != _lastNotifiedMoney)
            {
                _lastNotifiedMoney = _money;
                OnMoneyChanged?.Invoke(_money);
            }
            OnInventoryChanged?.Invoke();
        }

        private void NotifyAllChanged()
        {
            int currentWood = Wood;
            if (currentWood != _lastNotifiedWood)
            {
                _lastNotifiedWood = currentWood;
                OnWoodChanged?.Invoke(currentWood);
            }

            if (_money != _lastNotifiedMoney)
            {
                _lastNotifiedMoney = _money;
                OnMoneyChanged?.Invoke(_money);
            }

            OnInventoryChanged?.Invoke();
        }

        #region Universal Item Operations

        public int GetItemCount(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;

            int total = 0;
            var currentSlots = Slots;
            for (int i = 0; i < currentSlots.Count; i++)
            {
                var slot = currentSlots[i];
                if (!slot.IsEmpty && string.Equals(slot.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    total += slot.quantity;
                }
            }
            return total;
        }

        public bool HasItem(string itemId, int quantity = 1)
        {
            return GetItemCount(itemId) >= quantity;
        }

        public bool AddItem(string itemId, int quantity = 1)
        {
            if (string.IsNullOrEmpty(itemId) || quantity <= 0) return false;
            IInventoryItem item = ItemDatabase.GetItem(itemId);
            return AddItem(item, quantity);
        }

        public bool AddItem(IInventoryItem item, int quantity = 1)
        {
            if (item == null || quantity <= 0) return false;

            if (IsOffline || IsServerActive)
            {
                return ServerAddItem(item.Id, quantity);
            }
            else if (IsClientOnly)
            {
                CmdAddItem(item.Id, quantity);
                return true;
            }

            return false;
        }

        public bool RemoveItem(string itemId, int quantity = 1)
        {
            if (string.IsNullOrEmpty(itemId) || quantity <= 0) return false;

            if (IsOffline || IsServerActive)
            {
                return ServerRemoveItem(itemId, quantity);
            }
            else if (IsClientOnly)
            {
                if (GetItemCount(itemId) < quantity) return false;
                CmdRemoveItem(itemId, quantity);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Consumes or uses an item in the specified inventory slot polymorphically via IInventoryItem.Use().
        /// </summary>
        public bool UseItem(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= Slots.Count) return false;

            InventorySlot slot = Slots[slotIndex];
            if (slot.IsEmpty) return false;

            IInventoryItem item = slot.Item;
            if (item == null || !item.CanUse(gameObject)) return false;

            if (IsOffline || IsServerActive)
            {
                return ServerUseItem(slotIndex);
            }
            else if (IsClientOnly)
            {
                CmdUseItem(slotIndex);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Drops a specified quantity of items from an inventory slot into the game world.
        /// Strictly enforced: only items designated for both players (CharacterClass.Both) are droppable.
        /// </summary>
        public bool DropItem(int slotIndex, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= Slots.Count || quantity <= 0) return false;

            InventorySlot slot = Slots[slotIndex];
            if (slot.IsEmpty || slot.Item == null) return false;

            if (!slot.Item.IsDroppable)
            {
                Debug.LogWarning($"[PlayerInventory] Item '{slot.Item.DisplayName}' cannot be dropped. Weapons, Attributes, and Physical Upgrades are not droppable; only consumables for Both classes can be dropped.");
                return false;
            }

            int totalOwned = GetItemCount(slot.itemId);
            int toDrop = Mathf.Clamp(quantity, 1, totalOwned > 0 ? totalOwned : slot.quantity);

            if (IsOffline || IsServerActive)
            {
                return ServerDropItem(slotIndex, toDrop);
            }
            else if (IsClientOnly)
            {
                CmdDropItem(slotIndex, toDrop);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Removes a quantity from a specific inventory slot.
        /// </summary>
        public bool RemoveItemFromSlot(int slotIndex, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= Slots.Count || quantity <= 0) return false;

            if (IsOffline || IsServerActive)
            {
                return ServerRemoveItemFromSlot(slotIndex, quantity);
            }
            else if (IsClientOnly)
            {
                CmdRemoveItemFromSlot(slotIndex, quantity);
                return true;
            }

            return false;
        }

        #endregion

        #region Server & Offline Mutation Logic

        private bool ServerAddItem(string itemId, int quantity)
        {
            IInventoryItem item = ItemDatabase.GetItem(itemId);
            if (item == null || quantity <= 0) return false;

            int remaining = quantity;
            bool usesSync = IsServerActive && _syncSlots.Count > 0;

            // 1. Fill existing matching stacks first
            int count = usesSync ? _syncSlots.Count : _localSlots.Count;
            for (int i = 0; i < count; i++)
            {
                InventorySlot slot = usesSync ? _syncSlots[i] : _localSlots[i];
                if (!slot.IsEmpty && string.Equals(slot.itemId, item.Id, StringComparison.OrdinalIgnoreCase))
                {
                    if (slot.quantity < item.MaxStackSize)
                    {
                        int space = item.MaxStackSize - slot.quantity;
                        int toAdd = Mathf.Min(space, remaining);
                        slot.quantity += toAdd;
                        remaining -= toAdd;

                        SetSlot(i, slot, usesSync);

                        if (remaining <= 0) break;
                    }
                }
            }

            // 2. Place remaining into empty slots
            if (remaining > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    InventorySlot slot = usesSync ? _syncSlots[i] : _localSlots[i];
                    if (slot.IsEmpty)
                    {
                        int toAdd = Mathf.Min(item.MaxStackSize, remaining);
                        slot = new InventorySlot(item.Id, toAdd);
                        remaining -= toAdd;

                        SetSlot(i, slot, usesSync);

                        if (remaining <= 0) break;
                    }
                }
            }

            int added = quantity - remaining;
            if (added > 0)
            {
                OnItemAdded?.Invoke(new InventorySlot(item.Id, added));
                NotifySlotChanges();
                return true;
            }

            Debug.LogWarning($"<color=#f59e0b><b>[Inventory]</b> Inventory full! Could not add {quantity}x {item.DisplayName}.</color>");
            return false;
        }

        private bool ServerRemoveItem(string itemId, int quantity)
        {
            if (GetItemCount(itemId) < quantity) return false;

            int remaining = quantity;
            bool usesSync = IsServerActive && _syncSlots.Count > 0;
            int count = usesSync ? _syncSlots.Count : _localSlots.Count;

            // Remove from slots in reverse
            for (int i = count - 1; i >= 0; i--)
            {
                InventorySlot slot = usesSync ? _syncSlots[i] : _localSlots[i];
                if (!slot.IsEmpty && string.Equals(slot.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    int toRemove = Mathf.Min(slot.quantity, remaining);
                    slot.quantity -= toRemove;
                    remaining -= toRemove;

                    if (slot.quantity <= 0)
                    {
                        slot.Clear();
                    }

                    SetSlot(i, slot, usesSync);

                    if (remaining <= 0) break;
                }
            }

            OnItemRemoved?.Invoke(new InventorySlot(itemId, quantity));
            NotifySlotChanges();
            return true;
        }

        private bool ServerUseItem(int slotIndex)
        {
            bool usesSync = IsServerActive && _syncSlots.Count > 0;
            InventorySlot slot = usesSync ? _syncSlots[slotIndex] : _localSlots[slotIndex];

            if (slot.IsEmpty) return false;
            IInventoryItem item = slot.Item;
            if (item == null || !item.CanUse(gameObject)) return false;

            // Execute item's polymorphic behavior
            bool wasUsed = item.Use(gameObject);
            if (!wasUsed) return false;

            // Decrement stack
            slot.quantity--;
            if (slot.quantity <= 0)
            {
                slot.Clear();
            }

            SetSlot(slotIndex, slot, usesSync);
            NotifySlotChanges();
            return true;
        }

        private bool ServerDropItem(int slotIndex, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= Slots.Count || quantity <= 0) return false;

            InventorySlot slot = Slots[slotIndex];
            if (slot.IsEmpty || slot.Item == null) return false;
            if (!slot.Item.IsDroppable) return false;

            string itemId = slot.itemId;
            int totalOwned = GetItemCount(itemId);
            int toDrop = Mathf.Clamp(quantity, 1, totalOwned > 0 ? totalOwned : slot.quantity);

            int fromSlot = Mathf.Min(slot.quantity, toDrop);
            bool removed = ServerRemoveItemFromSlot(slotIndex, fromSlot);
            if (!removed) return false;

            int remainder = toDrop - fromSlot;
            if (remainder > 0)
            {
                ServerRemoveItem(itemId, remainder);
            }

            int startId = 0;
            int seed = UnityEngine.Random.Range(1000, 99999);
            
            if (string.Equals(itemId, "wood", StringComparison.OrdinalIgnoreCase) && Environment.WoodPickupPool.Instance != null)
            {
                startId = Environment.WoodPickupPool.GetNextWoodIdRange(toDrop);
            }
            else if ((string.Equals(itemId, "coin", StringComparison.OrdinalIgnoreCase) || string.Equals(itemId, "money", StringComparison.OrdinalIgnoreCase)) && Environment.CoinPickupPool.Instance != null)
            {
                startId = Environment.CoinPickupPool.GetNextCoinIdRange(toDrop);
            }
            else
            {
                // Generic item drop
                startId = Environment.GenericPickupPool.GetNextPickupId();
            }

            // Tell all clients (including host) to spawn the visual physical drop
            RpcSpawnWorldDrop(itemId, toDrop, transform.position + transform.forward * 1.2f + Vector3.up * 0.4f, startId, seed);
            return true;
        }

        private bool ServerRemoveItemFromSlot(int slotIndex, int quantity)
        {
            bool usesSync = IsServerActive && _syncSlots.Count > 0;
            InventorySlot slot = usesSync ? _syncSlots[slotIndex] : _localSlots[slotIndex];

            if (slot.IsEmpty || slot.quantity <= 0) return false;

            int toRemove = Mathf.Min(slot.quantity, quantity);
            slot.quantity -= toRemove;
            string removedId = slot.itemId;

            if (slot.quantity <= 0)
            {
                slot.Clear();
            }

            SetSlot(slotIndex, slot, usesSync);
            OnItemRemoved?.Invoke(new InventorySlot(removedId, toRemove));
            NotifySlotChanges();
            return true;
        }

        [ClientRpc]
        private void RpcSpawnWorldDrop(string itemId, int quantity, Vector3 dropOrigin, int startId, int seed)
        {
            SpawnWorldDrop(itemId, quantity, dropOrigin, startId, seed);
        }

        private void SpawnWorldDrop(string itemId, int quantity, Vector3 dropOrigin, int startId, int seed)
        {

            if (string.Equals(itemId, "wood", StringComparison.OrdinalIgnoreCase))
            {
                if (Environment.WoodPickupPool.Instance != null)
                {
                    Environment.WoodPickupPool.Instance.SpawnWood(dropOrigin, quantity, startId, seed);
                }
            }
            else if (string.Equals(itemId, "coin", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(itemId, "money", StringComparison.OrdinalIgnoreCase))
            {
                if (Environment.CoinPickupPool.Instance != null)
                {
                    Environment.CoinPickupPool.Instance.SpawnCoins(dropOrigin, quantity, startId, seed);
                }
            }
            else
            {
                // Generic Item Drop
                var def = ItemDatabase.GetItem(itemId);
                if (def != null && def.WorldDropPrefab != null)
                {
                    GameObject dropObj = Instantiate(def.WorldDropPrefab, dropOrigin, Quaternion.identity);
                    
                    // Add generic pickup logic if it doesn't already have one
                    if (!dropObj.TryGetComponent(out Environment.GenericItemPickup genericPickup))
                    {
                        genericPickup = dropObj.AddComponent<Environment.GenericItemPickup>();
                    }
                    
                    
                    genericPickup.Initialize(itemId, quantity, startId);
                }
                else
                {
                    Debug.LogWarning($"<color=#f59e0b>[PlayerInventory] Dropped {quantity}x '{itemId}' but it lacks a WorldDropPrefab in its ItemDefinition!</color>");
                }
            }
        }

        private void SetSlot(int index, InventorySlot slot, bool usesSync)
        {
            if (usesSync)
            {
                _syncSlots[index] = slot;
            }
            else
            {
                _localSlots[index] = slot;
            }
        }

        #endregion

        #region Mirror Network Commands

        [Command]
        private void CmdAddItem(string itemId, int quantity)
        {
            ServerAddItem(itemId, quantity);
        }

        [Command]
        private void CmdRemoveItem(string itemId, int quantity)
        {
            ServerRemoveItem(itemId, quantity);
        }

        [Command]
        private void CmdUseItem(int slotIndex)
        {
            ServerUseItem(slotIndex);
        }

        [Command]
        private void CmdDropItem(int slotIndex, int quantity)
        {
            ServerDropItem(slotIndex, quantity);
        }

        [Command]
        private void CmdRemoveItemFromSlot(int slotIndex, int quantity)
        {
            ServerRemoveItemFromSlot(slotIndex, quantity);
        }

        #endregion

        #region Backward-Compatible Currency & Wood Helpers

        public void AddWood(int amount)
        {
            if (amount <= 0) return;
            AddItem("wood", amount);
        }

        public bool RemoveWood(int amount)
        {
            if (amount <= 0 || Wood < amount) return false;
            return RemoveItem("wood", amount);
        }

        public void AddMoney(int amount)
        {
            if (amount <= 0) return;
            AddItem("coin", amount);
        }

        public bool RemoveMoney(int amount)
        {
            if (amount <= 0 || Money < amount) return false;
            return RemoveItem("coin", amount);
        }

        [Command]
        private void CmdAddMoney(int amount)
        {
            if (amount <= 0) return;
            ServerAddItem("coin", amount);
        }

        [Command]
        private void CmdRemoveMoney(int amount)
        {
            if (amount <= 0 || Money < amount) return;
            ServerRemoveItem("coin", amount);
        }

        /// <summary>
        /// Called when this player touches and collects a world coin pickup.
        /// Awards currency to the local player and notifies all other clients to despawn the same coin
        /// so it cannot be double-collected by a teammate.
        /// </summary>
        public void CollectWorldCoin(int coinId, int amount)
        {
            if (amount <= 0) return;

            if (IsOffline)
            {
                // Offline single-player: award directly, no sync needed
                ServerAddItem("coin", amount);
            }
            else if (IsServerActive)
            {
                // Host: verify with pool it wasn't already collected
                if (CoinPickupPool.Instance != null && !CoinPickupPool.Instance.ConsumeCoin(coinId)) return;
                
                // Award on server immediately, then tell all clients to remove coin
                ServerAddItem("coin", amount);
                RpcDespawnCoin(coinId);
            }
            else if (IsClientOnly)
            {
                // Client: send one command to server – server awards and despawns on all clients
                CmdCollectWorldCoin(coinId, amount);
            }
        }

        [Command]
        private void CmdCollectWorldCoin(int coinId, int amount)
        {
            if (amount <= 0) return;
            
            // Server: verify with pool it wasn't already collected by someone else
            if (CoinPickupPool.Instance != null && !CoinPickupPool.Instance.ConsumeCoin(coinId)) return;
            
            // Award currency to this player's inventory authoritatively
            ServerAddItem("coin", amount);
            // Broadcast: tell all clients (including the collector) to remove the coin from the world
            RpcDespawnCoin(coinId);
        }

        [ClientRpc]
        private void RpcDespawnCoin(int coinId)
        {
            if (coinId <= 0) return;

            // The collecting player's coin is already visually removed in CoinPickup.Collect().
            // Only peers (other clients / host) need to despawn the coin on their machine.
            if (netIdentity != null && netIdentity.isLocalPlayer) return;

            if (CoinPickupPool.Instance != null)
            {
                CoinPickupPool.Instance.DespawnCoin(coinId, transform);
            }
        }

        /// <summary>
        /// Called when this player touches and collects a world wood pickup.
        /// </summary>
        public void CollectWorldWood(int woodId, int amount)
        {
            if (amount <= 0) return;

            if (IsOffline)
            {
                ServerAddItem("wood", amount);
            }
            else if (IsServerActive)
            {
                if (Environment.WoodPickupPool.Instance != null && !Environment.WoodPickupPool.Instance.ConsumeWood(woodId)) return;
                
                ServerAddItem("wood", amount);
                RpcDespawnWood(woodId);
            }
            else if (IsClientOnly)
            {
                CmdCollectWorldWood(woodId, amount);
            }
        }

        [Command]
        private void CmdCollectWorldWood(int woodId, int amount)
        {
            if (amount <= 0) return;
            
            if (Environment.WoodPickupPool.Instance != null && !Environment.WoodPickupPool.Instance.ConsumeWood(woodId)) return;
            
            ServerAddItem("wood", amount);
            RpcDespawnWood(woodId);
        }

        [ClientRpc]
        private void RpcDespawnWood(int woodId)
        {
            if (woodId <= 0) return;

            if (netIdentity != null && netIdentity.isLocalPlayer) return;

            if (Environment.WoodPickupPool.Instance != null)
            {
                Environment.WoodPickupPool.Instance.DespawnWood(woodId, transform);
            }
        }

        /// <summary>
        /// Called when this player touches and collects a generic world pickup (potions, misc).
        /// </summary>
        public void CollectGenericPickup(int pickupId, string itemId, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(itemId)) return;

            if (IsOffline)
            {
                ServerAddItem(itemId, amount);
            }
            else if (IsServerActive)
            {
                if (Environment.GenericPickupPool.Instance != null && !Environment.GenericPickupPool.Instance.ConsumePickup(pickupId)) return;
                
                ServerAddItem(itemId, amount);
                RpcDespawnGenericPickup(pickupId);
            }
            else if (IsClientOnly)
            {
                CmdCollectGenericPickup(pickupId, itemId, amount);
            }
        }

        [Command]
        private void CmdCollectGenericPickup(int pickupId, string itemId, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(itemId)) return;
            
            if (Environment.GenericPickupPool.Instance != null && !Environment.GenericPickupPool.Instance.ConsumePickup(pickupId)) return;
            
            ServerAddItem(itemId, amount);
            RpcDespawnGenericPickup(pickupId);
        }

        [ClientRpc]
        private void RpcDespawnGenericPickup(int pickupId)
        {
            if (pickupId <= 0) return;

            if (netIdentity != null && netIdentity.isLocalPlayer) return;

            if (Environment.GenericPickupPool.Instance != null)
            {
                Environment.GenericPickupPool.Instance.DespawnPickup(pickupId, transform);
            }
        }

        #endregion
    }
}
