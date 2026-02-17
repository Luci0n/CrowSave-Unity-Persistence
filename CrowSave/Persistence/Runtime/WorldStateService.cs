using System;
using System.Collections.Generic;
using CrowSave.Persistence.Core;

namespace CrowSave.Persistence.Runtime
{
/// <summary>
/// Thin service wrapper around the RAM state.
/// Central place to mutate blobs, tombstones, and disk eligibility.
///
/// Usage:
/// - Saving/updating state: call SetEntityBlob(...) (disk captures) or SetEntityBlob_NoEligibilityChange(...) (RAM-only captures).
/// - Disk eligibility: call SetDiskEligibilityOnly(...) when you need to update whether an entity should be written to disk
///   without recapturing a blob (e.g., CaptureDirty skipped it because it wasn't dirty).
/// - Permanent destruction (tombstones): when a persistent object should stay gone across loads (pickups collected, one-time
///   breakables, etc.), call MarkDestroyed(entityKey) before destroying/disable-removing it. Tombstoned ids will be destroyed
///   on apply/catch-up and their blobs will not be applied (prevents resurrection).
/// - Optional undo/respawn: call UnmarkDestroyed(entityKey) if you intentionally want an id to exist again.
/// </summary>
    public sealed class WorldStateService
    {
        public WorldStateRAM State { get; } = new WorldStateRAM();

        public void SetEntityBlob(EntityKey key, byte[] blob, bool diskEligible)
        {
            ValidateKey(key);

            var scope = State.GetOrCreate(key.ScopeKey);

            scope.EntityBlobs[key.EntityId] = blob;
            scope.Destroyed.Remove(key.EntityId);

            SetEligibility(scope, key.EntityId, diskEligible);

            scope.BumpRevision();
        }

        public void SetEntityBlob_NoEligibilityChange(EntityKey key, byte[] blob)
        {
            ValidateKey(key);

            var scope = State.GetOrCreate(key.ScopeKey);

            scope.EntityBlobs[key.EntityId] = blob;
            scope.Destroyed.Remove(key.EntityId);

            scope.BumpRevision();
        }

        public void SetDiskEligibilityOnly(EntityKey key, bool diskEligible)
        {
            if (string.IsNullOrEmpty(key.ScopeKey)) throw new ArgumentNullException(nameof(key.ScopeKey));
            if (string.IsNullOrEmpty(key.EntityId)) throw new ArgumentNullException(nameof(key.EntityId));

            if (!State.TryGet(key.ScopeKey, out var scope))
                return;

            bool changed = SetEligibility(scope, key.EntityId, diskEligible);
            if (changed) scope.BumpRevision();
        }

        public bool TryHasEntityBlob(EntityKey key)
        {
            if (string.IsNullOrEmpty(key.ScopeKey)) return false;
            if (string.IsNullOrEmpty(key.EntityId)) return false;

            if (!State.TryGet(key.ScopeKey, out var scope))
                return false;

            return scope.EntityBlobs.ContainsKey(key.EntityId);
        }

        public bool IsDestroyed(EntityKey key)
        {
            if (string.IsNullOrEmpty(key.ScopeKey)) return false;
            if (string.IsNullOrEmpty(key.EntityId)) return false;

            if (!State.TryGet(key.ScopeKey, out var scope))
                return false;

            return scope.Destroyed.Contains(key.EntityId);
        }

        public void RemoveEntityBlob(EntityKey key)
        {
            if (string.IsNullOrEmpty(key.ScopeKey)) return;
            if (string.IsNullOrEmpty(key.EntityId)) return;

            if (!State.TryGet(key.ScopeKey, out var scope))
                return;

            bool changed = false;

            if (scope.EntityBlobs.Remove(key.EntityId))
                changed = true;

            if (scope.DiskEligible.Remove(key.EntityId))
                changed = true;

            if (changed)
                scope.BumpRevision();
        }

        public void MarkDestroyed(EntityKey key, bool removeBlobAndEligibility = true)
        {
            ValidateKey(key);

            var scope = State.GetOrCreate(key.ScopeKey);

            bool changed = scope.Destroyed.Add(key.EntityId);

            if (removeBlobAndEligibility)
            {
                if (scope.EntityBlobs.Remove(key.EntityId))
                    changed = true;

                if (scope.DiskEligible.Remove(key.EntityId))
                    changed = true;
            }

            if (changed)
                scope.BumpRevision();
        }

        public void UnmarkDestroyed(EntityKey key)
        {
            ValidateKey(key);

            if (!State.TryGet(key.ScopeKey, out var scope))
                return;

            bool changed = scope.Destroyed.Remove(key.EntityId);
            if (changed) scope.BumpRevision();
        }

        public void ClearScope(string scopeKey)
        {
            if (string.IsNullOrWhiteSpace(scopeKey))
                return;

            State.ClearScope(scopeKey);
        }

        public void ClearAll()
        {
            State.ClearAll();
        }

        public void PruneToLiveSet(string scopeKey, HashSet<string> liveEntityIds, bool pruneBlobsToo)
        {
            if (string.IsNullOrWhiteSpace(scopeKey)) return;
            if (liveEntityIds == null) throw new ArgumentNullException(nameof(liveEntityIds));

            if (!State.TryGet(scopeKey, out var scope))
                return;

            bool changed = false;

            // Remove disk eligibility for ids that no longer exist.
            if (scope.DiskEligible.RemoveWhere(id => !liveEntityIds.Contains(id)) > 0)
                changed = true;

            if (pruneBlobsToo)
            {
                // Optional: also remove blobs for ids that no longer exist.
                var keys = new System.Collections.Generic.List<string>();
                foreach (var id in scope.EntityBlobs.Keys)
                    if (!liveEntityIds.Contains(id))
                        keys.Add(id);

                for (int i = 0; i < keys.Count; i++)
                {
                    if (scope.EntityBlobs.Remove(keys[i]))
                        changed = true;
                }
            }

            if (changed)
                scope.BumpRevision();
        }

        private static void ValidateKey(EntityKey key)
        {
            if (string.IsNullOrEmpty(key.ScopeKey)) throw new ArgumentNullException(nameof(key.ScopeKey));
            if (string.IsNullOrEmpty(key.EntityId)) throw new ArgumentNullException(nameof(key.EntityId));
        }

        private static bool SetEligibility(ScopeState scope, string entityId, bool diskEligible)
        {
            if (diskEligible)
                return scope.DiskEligible.Add(entityId);

            return scope.DiskEligible.Remove(entityId);
        }
    }
}
