using System.Collections.Generic;
using UnityEngine;

namespace Woodsmen.Environment
{
    /// <summary>
    /// Centralized registry for all active GenericItemPickup objects (dropped consumables, potions, etc.)
    ///
    /// Follows the same "claim-then-despawn" pattern as CoinPickupPool / WoodPickupPool:
    ///   - ConsumePickup() atomically marks an ID as claimed → only the first player wins.
    ///   - DespawnPickup() does the actual visual removal so the host's pickup is
    ///     still findable by ID even after ConsumePickup() has run on the same frame.
    /// </summary>
    public class GenericPickupPool : MonoBehaviour
    {
        private static GenericPickupPool _instance;

        public static GenericPickupPool Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<GenericPickupPool>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[GenericPickup_Registry]");
                        _instance = go.AddComponent<GenericPickupPool>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ─── ID Generation ────────────────────────────────────────────────────────

        private static int _nextPickupId = 1;

        /// <summary>Generates a unique pickup ID on the server.</summary>
        public static int GetNextPickupId() => _nextPickupId++;

        // ─── Active Pickup Tracking ───────────────────────────────────────────────

        /// <summary>All currently live GenericItemPickups in the world, keyed by pickupId.</summary>
        private readonly Dictionary<int, GenericItemPickup> _activeById =
            new Dictionary<int, GenericItemPickup>();

        /// <summary>
        /// IDs already claimed by ConsumePickup() but not yet visually removed.
        /// Kept separate from _activeById so DespawnPickup() can still find the
        /// pickup instance on the host after ConsumePickup() has run server-side.
        /// </summary>
        private readonly HashSet<int> _consumedIds = new HashSet<int>();

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>Registers a newly spawned pickup so it can be claimed / despawned by ID.</summary>
        public void Register(int pickupId, GenericItemPickup pickup)
        {
            if (pickupId <= 0 || pickup == null) return;
            _activeById[pickupId] = pickup;
        }

        /// <summary>Removes the pickup from the registry (called when the object is destroyed locally).</summary>
        public void Unregister(int pickupId)
        {
            if (pickupId <= 0) return;
            _activeById.Remove(pickupId);
            _consumedIds.Remove(pickupId);
        }

        /// <summary>
        /// Server-side atomic claim.  Returns true exactly ONCE per pickupId.
        /// Does NOT remove from _activeById so DespawnPickup() can still find the instance.
        /// </summary>
        public bool ConsumePickup(int pickupId)
        {
            if (pickupId <= 0) return true;   // untracked offline pickups always succeed
            if (_activeById.ContainsKey(pickupId) && _consumedIds.Add(pickupId))
                return true;   // first caller wins
            return false;      // already claimed or unknown
        }

        /// <summary>
        /// Visually removes the pickup on this machine (called via ClientRpc on all peers).
        /// Cleans up both tracking structures.
        /// </summary>
        public void DespawnPickup(int pickupId, Transform collectorTransform)
        {
            if (pickupId <= 0) return;
            if (_activeById.TryGetValue(pickupId, out GenericItemPickup pickup))
            {
                _activeById.Remove(pickupId);
                _consumedIds.Remove(pickupId);
                if (pickup != null)
                    pickup.RemoteCollect(collectorTransform);
            }
        }
    }
}
