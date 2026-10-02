using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Woodsmen.Environment
{
    /// <summary>
    /// Centralized network authority for ALL destructible trees in the scene.
    ///
    /// Design rationale
    /// ────────────────
    /// Mirror carries significant per-object overhead for every NetworkIdentity:
    /// spawn messages, object tracking, and SyncVar dirty checks on every frame.
    /// With hundreds or thousands of trees in the world, giving each tree its own
    /// NetworkIdentity would easily saturate the network and crash the session.
    ///
    /// Instead, TreeSyncManager is the ONLY NetworkBehaviour for trees.
    /// It holds a flat registry of every DestructibleTree by a simple integer ID
    /// (assigned sequentially on Start) and routes chop/fell events via Commands and
    /// ClientRpcs that carry just the tree ID, damage, and hit position — a handful
    /// of bytes per chop regardless of scene tree count.
    ///
    /// Usage
    /// ─────
    /// - Place one TreeSyncManager GameObject in your scene (or in a persistent manager
    ///   prefab). It auto-discovers all DestructibleTree instances at runtime.
    /// - No changes required to DestructibleTree prefabs — they call
    ///   TreeSyncManager.Register() in their Start() method automatically.
    /// - No NetworkIdentity required on any tree prefab or tree GameObject.
    /// </summary>
    public class TreeSyncManager : NetworkBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────────

        private static TreeSyncManager _instance;

        public static TreeSyncManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = FindFirstObjectByType<TreeSyncManager>();
                return _instance;
            }
        }

        /// <summary>True when a Mirror session (host or client) is currently active.</summary>
        public static bool IsNetworked => NetworkClient.active || NetworkServer.active;

        // ─── Registry ─────────────────────────────────────────────────────────────

        // Map: stable positionHash treeId → DestructibleTree
        private static readonly Dictionary<int, DestructibleTree> _trees =
            new Dictionary<int, DestructibleTree>(256);

        // ─── Static Registration API (called by DestructibleTree.Start / OnDestroy) ─

        /// <summary>
        /// Registers a tree using the position-based ID it already computed in Start().
        /// Safe to call before the TreeSyncManager instance is ready.
        /// </summary>
        public static void Register(DestructibleTree tree)
        {
            if (tree == null || tree.TreeId < 0) return;
            _trees[tree.TreeId] = tree;
        }

        /// <summary>
        /// Removes a tree from the registry when it is destroyed.
        /// </summary>
        public static void Unregister(DestructibleTree tree)
        {
            if (tree == null || tree.TreeId < 0) return;
            _trees.Remove(tree.TreeId);
        }

        // ─── Unity Lifecycle ─────────────────────────────────────────────────────

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

        // ─── Public API (called by DestructibleTree.Chop) ────────────────────────

        /// <summary>
        /// Entry point: a client (or the host) requests that the server process a chop.
        /// If this machine IS the server, apply immediately; otherwise send a Command.
        /// </summary>
        public void RequestChop(int treeId, float damage, Vector3 hitPoint)
        {
            if (isServer)
            {
                ServerHandleChop(treeId, damage, hitPoint);
            }
            else
            {
                CmdChop(treeId, damage, hitPoint);
            }
        }

        // ─── Server Authority ─────────────────────────────────────────────────────

        /// <summary>
        /// Client sends a chop request to the server. requiresAuthority = false means
        /// any client can send this regardless of who "owns" the manager object.
        /// </summary>
        [Command(requiresAuthority = false)]
        private void CmdChop(int treeId, float damage, Vector3 hitPoint)
        {
            ServerHandleChop(treeId, damage, hitPoint);
        }

        /// <summary>
        /// Server-side logic: validates the hit, updates HP, and broadcasts the result
        /// to all clients. Only the server runs this.
        /// </summary>
        private void ServerHandleChop(int treeId, float damage, Vector3 hitPoint)
        {
            if (!_trees.TryGetValue(treeId, out DestructibleTree tree)) return;
            if (tree == null || tree.IsFelled) return;

            // Apply health reduction on the server's local copy to stay authoritative.
            // We track HP in the tree's own fields (no SyncVar needed — we replicate
            // events, not state, keeping bandwidth minimal).
            float hpBefore = tree.CurrentHealth;
            tree.ApplyChopLocally(damage, hitPoint);
            float hpAfter = tree.CurrentHealth;

            bool felled = hpAfter <= 0f || tree.IsFelled;

            if (felled)
            {
                // Calculate wood drop now on the server with a shared seed
                int dropMin = Mathf.Min(tree.MinWoodDrop, tree.MaxWoodDrop);
                int dropMax = Mathf.Max(tree.MinWoodDrop, tree.MaxWoodDrop);
                int dropCount = Random.Range(dropMin, dropMax + 1);

                // Assign network-tracked IDs so neither player can double-collect
                int startWoodId = WoodPickupPool.GetNextWoodIdRange(dropCount);
                int seed = Random.Range(1, int.MaxValue);

                Debug.Log($"[TreeSyncManager] Tree #{treeId} felled. Dropping {dropCount} wood (ids {startWoodId}-{startWoodId + dropCount - 1}).");

                // Tell all clients to fell the tree and spawn wood with matching IDs/seed
                RpcFellTree(treeId, dropCount, hitPoint, startWoodId, seed);
            }
            else
            {
                // Just broadcast the hit feedback (wobble / VFX / sound) to all clients.
                // The HP reduction is already applied on the server; clients only need visuals.
                RpcOnHit(treeId, hitPoint);
            }
        }

        // ─── Client Replication ───────────────────────────────────────────────────

        /// <summary>
        /// Broadcasts a chop hit to all clients: plays wobble, VFX, and audio.
        /// </summary>
        [ClientRpc]
        private void RpcOnHit(int treeId, Vector3 hitPoint)
        {
            if (!_trees.TryGetValue(treeId, out DestructibleTree tree)) return;
            if (tree == null || tree.IsFelled) return;

            // Always play feedback on every client (including host).
            // The server's ApplyChopLocally already played it locally, but calling it
            // again here would double it on the host, so skip there.
            if (!isServer)
            {
                tree.PlayHitFeedback(hitPoint);
            }
        }

        /// <summary>
        /// Broadcasts tree felling to all clients.
        /// Every client destroys the tree locally and spawns wood pickups.
        /// </summary>
        [ClientRpc]
        private void RpcFellTree(int treeId, int woodCount, Vector3 origin, int startWoodId, int seed)
        {
            // Spawn wood on every machine with matching IDs and seed so both clients see
            // identical pickups and the server can prevent double-collection.
            if (WoodPickupPool.Instance != null)
            {
                WoodPickupPool.Instance.SpawnWood(origin, woodCount, startWoodId, seed);
            }

            // Destroy the tree on every client.
            // On the server-host, FellTree was already called inside ApplyChopLocally,
            // so we skip it there to avoid a double-destroy.
            if (isServer) return;

            if (_trees.TryGetValue(treeId, out DestructibleTree tree) && tree != null)
            {
                tree.FellTree();
            }
        }
    }
}
