using System;
using System.Collections.Generic;
using System.Linq;
using CrowSave.Persistence.Core;
using UnityEngine;

namespace CrowSave.Persistence.Runtime
{
    public sealed class CaptureApplyService
    {
        private readonly PersistenceRegistry _registry;
        private readonly WorldStateService _world;

        private readonly HashSet<string> _appliedScopes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, ApplyReason> _lastApplyReason = new Dictionary<string, ApplyReason>(StringComparer.Ordinal);

        private readonly HashSet<string> _scopesApplying = new HashSet<string>(StringComparer.Ordinal);

        // Reusable buffers to reduce GC
        private readonly List<RegisteredEntity> _buffer = new List<RegisteredEntity>(256);

        // Reusable live-id set (for pruning on disk captures)
        private readonly HashSet<string> _liveIds = new HashSet<string>(StringComparer.Ordinal);

        public CaptureApplyService(PersistenceRegistry registry, WorldStateService world)
        {
            _registry = registry;
            _world = world;

            if (_registry != null)
                _registry.Registered += OnRegisteredCatchUp;
        }

        private static bool IsDiskIntent(CaptureIntent intent)
            => intent == CaptureIntent.DiskManualSave
            || intent == CaptureIntent.DiskAutosave
            || intent == CaptureIntent.DiskCheckpoint;

        private static bool IsDiskEligible(PersistencePolicy policy, CaptureIntent intent)
        {
            // Never/SessionOnly should never hit disk.
            if (policy == PersistencePolicy.Never || policy == PersistencePolicy.SessionOnly)
                return false;

            // SaveGame is eligible for all disk intents.
            if (policy == PersistencePolicy.SaveGame)
                return intent == CaptureIntent.DiskManualSave
                    || intent == CaptureIntent.DiskAutosave
                    || intent == CaptureIntent.DiskCheckpoint;

            // CheckpointOnly is ONLY eligible for checkpoint disk writes.
            if (policy == PersistencePolicy.CheckpointOnly)
                return intent == CaptureIntent.DiskCheckpoint;

            return false;
        }

        // Back-compat convenience overloads (optional but harmless)
        public void CaptureDirty(string scopeKey) => CaptureDirty(scopeKey, CaptureIntent.DiskManualSave);
        public void CaptureAll(string scopeKey) => CaptureAll(scopeKey, CaptureIntent.DiskManualSave);

        public void CaptureDirty(string scopeKey, CaptureIntent intent)
        {
            if (string.IsNullOrWhiteSpace(scopeKey)) return;

            _registry.CopyScopeToList(scopeKey, _buffer);

            // On DISK captures: prune eligibility for entities that no longer exist in the registry.
            // This prevents stale DiskEligible entries from staying "sticky" across lifecycle changes.
            if (IsDiskIntent(intent))
                PruneDiskEligibilityToLive(scopeKey);

            int captured = 0, skippedNotDirty = 0, skippedNever = 0;

            foreach (var re in _buffer)
            {
                if (!IsAlive(re)) continue;

                var policy = re.Entity.Policy;

                if (policy == PersistencePolicy.Never)
                {
                    _world.RemoveEntityBlob(re.Key);
                    skippedNever++;
                    continue;
                }

                // If we're doing a DISK capture but skipping due to not-dirty,
                // we STILL must update eligibility (otherwise it becomes sticky).
                if (re.Entity is IDirtyPersistent dp && !dp.IsDirty)
                {
                    if (IsDiskIntent(intent))
                        _world.SetDiskEligibilityOnly(re.Key, IsDiskEligible(policy, intent));

                    skippedNotDirty++;
                    continue;
                }

                var blob = CaptureEntity(re.Entity);

                if (IsDiskIntent(intent))
                    _world.SetEntityBlob(re.Key, blob, diskEligible: IsDiskEligible(policy, intent));
                else
                    _world.SetEntityBlob_NoEligibilityChange(re.Key, blob);

                captured++;

                if (re.Entity is IDirtyPersistent dp2) dp2.ClearDirty();
            }

            PersistenceLog.Info(
                $"CAPTURE DIRTY scope='{scopeKey}' intent={intent} captured={captured} skipNever={skippedNever} skipNotDirty={skippedNotDirty}"
            );
        }

        public void CaptureAll(string scopeKey, CaptureIntent intent)
        {
            if (string.IsNullOrWhiteSpace(scopeKey)) return;

            _registry.CopyScopeToList(scopeKey, _buffer);

            // On DISK captures: prune eligibility for entities that no longer exist in the registry.
            if (IsDiskIntent(intent))
                PruneDiskEligibilityToLive(scopeKey);

            int captured = 0;
            int skippedNever = 0;

            foreach (var re in _buffer)
            {
                if (!IsAlive(re)) continue;

                var policy = re.Entity.Policy;

                if (policy == PersistencePolicy.Never)
                {
                    _world.RemoveEntityBlob(re.Key);
                    skippedNever++;
                    continue;
                }

                var blob = CaptureEntity(re.Entity);

                if (IsDiskIntent(intent))
                    _world.SetEntityBlob(re.Key, blob, diskEligible: IsDiskEligible(policy, intent));
                else
                    _world.SetEntityBlob_NoEligibilityChange(re.Key, blob);

                captured++;

                if (re.Entity is IDirtyPersistent dp) dp.ClearDirty();
            }

            PersistenceLog.Info($"CAPTURE ALL scope='{scopeKey}' intent={intent} captured={captured} skipNever={skippedNever}");
        }

        private void PruneDiskEligibilityToLive(string scopeKey)
        {
            _liveIds.Clear();

            for (int i = 0; i < _buffer.Count; i++)
            {
                var re = _buffer[i];
                if (!IsAlive(re)) continue;
                _liveIds.Add(re.Id.EntityId);
            }

            _world.PruneToLiveSet(scopeKey, _liveIds, pruneBlobsToo: false);
        }

        private static byte[] CaptureEntity(IPersistentEntity entity)
        {
            using var w = new StateBinaryWriter(256);
            entity.Capture(w);
            return w.ToArray();
        }

        public void ApplyScope(string scopeKey, ApplyReason reason)
        {
            if (string.IsNullOrWhiteSpace(scopeKey))
                return;

            if (_scopesApplying.Contains(scopeKey))
            {
                PersistenceLog.Warn($"APPLY scope='{scopeKey}' skipped (already applying).");
                return;
            }

            if (!_world.State.TryGet(scopeKey, out var scopeState))
            {
                PersistenceLog.Info($"APPLY scope='{scopeKey}' no ScopeState in RAM (nothing to apply).");
                return;
            }

            _scopesApplying.Add(scopeKey);

            try
            {
                // Snapshot live set once (non-alloc list copy).
                _registry.CopyScopeToList(scopeKey, _buffer);

                // Phase 1: Destroy (but don't rely on immediate removal)
                for (int i = 0; i < _buffer.Count; i++)
                {
                    var re = _buffer[i];
                    if (!IsAlive(re)) continue;

                    if (!scopeState.Destroyed.Contains(re.Id.EntityId)) continue;

                    PersistenceLog.Info($"APPLY DESTROY {scopeKey}:{re.Id.EntityId} -> {SafeName(re)}", re.Id);
                    if (re.Id != null && re.Id.gameObject != null)
                        UnityEngine.Object.Destroy(re.Id.gameObject);
                }

                // Re-snapshot after destroy requests.
                _registry.CopyScopeToList(scopeKey, _buffer);

                bool BlobExists(RegisteredEntity re2) =>
                    re2 != null && re2.Id != null &&
                    scopeState.EntityBlobs.ContainsKey(re2.Id.EntityId);

                // Phase 2a: Reset rules on DiskLoad
                if (reason == ApplyReason.DiskLoad)
                {
                    for (int i = 0; i < _buffer.Count; i++)
                    {
                        var re = _buffer[i];
                        if (!IsAlive(re)) continue;

                        if (re.Entity is not IResettablePersistent rp)
                            continue;

                        bool blobExists = BlobExists(re);

                        if (rp.ResetPolicy == ResetPolicy.ResetAlwaysOnDiskLoad)
                        {
                            rp.ResetState(ApplyReason.DiskLoad);
                            continue;
                        }

                        if (!blobExists && rp.ResetPolicy == ResetPolicy.ResetOnMissingOnDiskLoad)
                        {
                            rp.ResetState(ApplyReason.DiskLoad);
                        }
                    }
                }

                // Phase 2b: Apply blobs deterministically.
                var applyList = _buffer
                    .Where(re =>
                    {
                        if (!IsAlive(re)) return false;

                        if (scopeState.Destroyed.Contains(re.Id.EntityId))
                            return false;

                        if (re.Entity.Policy == PersistencePolicy.Never)
                        {
                            _world.RemoveEntityBlob(re.Key);
                            return false;
                        }

                        if (!BlobExists(re)) return false;

                        if (reason != ApplyReason.DiskLoad) return true;

                        if (re.Entity is IResettablePersistent rp &&
                            rp.ResetPolicy == ResetPolicy.ResetAlwaysOnDiskLoad)
                            return false;

                        return true;
                    })
                    .OrderBy(e => e.Entity.Priority)
                    .ThenBy(e => e.Id.EntityId, StringComparer.Ordinal)
                    .ToList();

                foreach (var re in applyList)
                {
                    var blob = scopeState.EntityBlobs[re.Id.EntityId];
                    if (blob == null) continue;

                    TryApplyBlobToEntity(scopeKey, re, blob, reason);
                }

                _appliedScopes.Add(scopeKey);
                _lastApplyReason[scopeKey] = reason;

                PersistenceLog.Info($"APPLY DONE scope='{scopeKey}' reason={reason}");
            }
            finally
            {
                _scopesApplying.Remove(scopeKey);
            }
        }

        private void OnRegisteredCatchUp(RegisteredEntity re, bool replaced)
        {
            if (!IsAlive(re)) return;

            string scopeKey = re.Id.ScopeKey;
            if (string.IsNullOrWhiteSpace(scopeKey)) return;

            if (!_appliedScopes.Contains(scopeKey)) return;
            if (_scopesApplying.Contains(scopeKey)) return;

            if (!_world.State.TryGet(scopeKey, out var scopeState)) return;

            if (scopeState.Destroyed.Contains(re.Id.EntityId))
            {
                PersistenceLog.Info($"CATCH-UP DESTROY {scopeKey}:{re.Id.EntityId} -> {SafeName(re)}", re.Id);
                if (re.Id != null && re.Id.gameObject != null)
                    UnityEngine.Object.Destroy(re.Id.gameObject);
                return;
            }

            if (re.Entity.Policy == PersistencePolicy.Never)
            {
                _world.RemoveEntityBlob(re.Key);
                return;
            }

            _lastApplyReason.TryGetValue(scopeKey, out var reason);

            if (reason == ApplyReason.DiskLoad && re.Entity is IResettablePersistent rp)
            {
                bool blobExists = scopeState.EntityBlobs.ContainsKey(re.Id.EntityId);

                if (rp.ResetPolicy == ResetPolicy.ResetAlwaysOnDiskLoad)
                {
                    rp.ResetState(ApplyReason.DiskLoad);
                    return;
                }

                if (!blobExists && rp.ResetPolicy == ResetPolicy.ResetOnMissingOnDiskLoad)
                {
                    rp.ResetState(ApplyReason.DiskLoad);
                    return;
                }
            }

            if (!scopeState.EntityBlobs.TryGetValue(re.Id.EntityId, out var blob) || blob == null)
                return;

            if (reason == ApplyReason.DiskLoad &&
                re.Entity is IResettablePersistent rp2 &&
                rp2.ResetPolicy == ResetPolicy.ResetAlwaysOnDiskLoad)
                return;

            PersistenceLog.Info($"CATCH-UP APPLY {scopeKey}:{re.Id.EntityId} -> {SafeName(re)} (reason={reason})", re.Id);
            TryApplyBlobToEntity(scopeKey, re, blob, reason);
        }

        private void TryApplyBlobToEntity(string scopeKey, RegisteredEntity re, byte[] blob, ApplyReason reason)
        {
            if (!IsAlive(re) || blob == null) return;

            try
            {
                using var r = new StateBinaryReader(blob);

                if (re.Entity is IApplyReasonedPersistent ar)
                    ar.Apply(r, reason);
                else
                    re.Entity.Apply(r);

                if (re.Entity is IDirtyPersistent dp) dp.ClearDirty();
            }
            catch (Exception ex)
            {
                PersistenceLog.Error($"APPLY FAIL {scopeKey}:{re.Id.EntityId} -> {SafeName(re)}: {ex.Message}", re.Id);
            }
        }

        private static bool IsAlive(RegisteredEntity re)
        {
            if (re == null) return false;
            if (re.Id == null) return false; // Unity destroyed behaves like null

            if (re.Entity == null) return false;
            if (re.Entity is UnityEngine.Object uo && uo == null) return false;

            return true;
        }

        private static string SafeName(RegisteredEntity re)
        {
            if (re == null || re.Id == null) return "(null/destroyed)";
            return re.Id.name;
        }
    }
}
