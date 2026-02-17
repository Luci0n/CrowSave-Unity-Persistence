using System;
using System.Collections.Generic;
using System.Linq;
using CrowSave.Persistence.Core;
using UnityEngine;

namespace CrowSave.Persistence.Runtime
{
    public sealed class RegisteredEntity
    {
        public readonly PersistentId Id;
        public readonly IPersistentEntity Entity;

        public RegisteredEntity(PersistentId id, IPersistentEntity entity)
        {
            Id = id;
            Entity = entity;
        }

        public EntityKey Key => new EntityKey(Id.ScopeKey, Id.EntityId);
    }

    /// <summary>
    /// Tracks live persistent entities for currently loaded scopes.
    ///
    /// Features:
    /// - Scope revision counters (increment on register/unregister/replace).
    /// - Events for late-registration catch-up apply.
    /// - Safe handling of Unity "destroyed but referenced" objects.
    /// - Deterministic duplicate handling (refuse true duplicates; allow replace if old entry is dead).
    ///
    /// Adds:
    /// - Non-alloc enumeration APIs to avoid GC churn in barriers/capture/apply.
    /// </summary>
    public sealed class PersistenceRegistry
    {
        private readonly Dictionary<string, Dictionary<string, RegisteredEntity>> _byScope
            = new Dictionary<string, Dictionary<string, RegisteredEntity>>(StringComparer.Ordinal);

        private readonly Dictionary<string, int> _scopeRevision
            = new Dictionary<string, int>(StringComparer.Ordinal);

        public event Action<RegisteredEntity, bool> Registered;
        public event Action<PersistentId> Unregistered;

        public void Register(PersistentId id, IPersistentEntity entity)
        {
            if (!IsAlive(id)) return;
            if (!IsAlive(entity)) return;

            if (!id.HasValidId)
            {
                PersistenceLog.Warn("Register skipped (missing ID) on PersistentId.", id);
                return;
            }

            string scope = SafeScopeKey(id);

            if (!_byScope.TryGetValue(scope, out var dict))
            {
                dict = new Dictionary<string, RegisteredEntity>(StringComparer.Ordinal);
                _byScope[scope] = dict;
            }

            if (dict.TryGetValue(id.EntityId, out var existing))
            {
                bool existingAlive = existing != null && IsAlive(existing.Id) && IsAlive(existing.Entity);
                bool sameIdObject = existing != null && ReferenceEquals(existing.Id, id);

                if (!existingAlive || sameIdObject)
                {
                    var re2 = new RegisteredEntity(id, entity);
                    dict[id.EntityId] = re2;

                    BumpScopeRevision(scope);

                    PersistenceLog.Info(
                        sameIdObject
                            ? $"REGISTER (refresh) {scope}:{id.EntityId} -> {SafeName(id)}"
                            : $"REGISTER (replaced-dead) {scope}:{id.EntityId} -> {SafeName(id)}",
                        id
                    );

                    Registered?.Invoke(re2, true);
                    return;
                }

                PersistenceLog.Error(
                    $"DUPLICATE PersistentId: refusing registration (deterministic).\n" +
                    $"Scope='{scope}' EntityId='{id.EntityId}'\n" +
                    $"Existing='{SafeName(existing)}' New='{SafeName(id)}'",
                    id
                );

#if UNITY_EDITOR
                Debug.Break();
#endif
                return;
            }

            var re = new RegisteredEntity(id, entity);
            dict[id.EntityId] = re;

            BumpScopeRevision(scope);

            PersistenceLog.Info($"REGISTER {scope}:{id.EntityId} -> {SafeName(id)}", id);
            Registered?.Invoke(re, false);
        }

        public void Unregister(PersistentId id)
        {
            if (!IsAlive(id) || !id.HasValidId) return;

            string scope = SafeScopeKey(id);

            if (_byScope.TryGetValue(scope, out var dict))
            {
                bool removed = dict.Remove(id.EntityId);
                if (removed)
                {
                    BumpScopeRevision(scope);
                    PersistenceLog.Info($"UNREGISTER {scope}:{id.EntityId} -> {SafeName(id)}", id);
                    Unregistered?.Invoke(id);
                }

                if (dict.Count == 0) _byScope.Remove(scope);
            }
        }

        /// <summary>
        /// Non-alloc enumeration over live entities in a scope.
        /// IMPORTANT: enumerate immediately; do not store the returned Values reference.
        /// </summary>
        public IEnumerable<RegisteredEntity> EnumerateScope(string scopeKey)
        {
            scopeKey ??= "";
            if (_byScope.TryGetValue(scopeKey, out var dict))
                return dict.Values;
            return Array.Empty<RegisteredEntity>();
        }

        /// <summary>
        /// Non-alloc copy into a caller-provided list (caller can reuse the list).
        /// </summary>
        public void CopyScopeToList(string scopeKey, List<RegisteredEntity> dst)
        {
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            dst.Clear();

            scopeKey ??= "";
            if (_byScope.TryGetValue(scopeKey, out var dict))
            {
                foreach (var v in dict.Values)
                    dst.Add(v);
            }
        }

        /// <summary>
        /// Allocating snapshot list (kept for convenience; avoid in hot paths).
        /// </summary>
        public IReadOnlyList<RegisteredEntity> GetAllInScope(string scopeKey)
        {
            scopeKey ??= "";
            if (_byScope.TryGetValue(scopeKey, out var dict))
                return dict.Values.ToList();
            return new List<RegisteredEntity>();
        }

        public bool TryGet(string scopeKey, string entityId, out RegisteredEntity entity)
        {
            entity = null;
            if (entityId == null) return false;

            scopeKey ??= "";
            if (!_byScope.TryGetValue(scopeKey, out var dict)) return false;

            return dict.TryGetValue(entityId, out entity);
        }

        public int GetScopeCount(string scopeKey)
        {
            scopeKey ??= "";
            return _byScope.TryGetValue(scopeKey, out var dict) ? dict.Count : 0;
        }

        public int GetScopeRevision(string scopeKey)
        {
            scopeKey ??= "";
            return _scopeRevision.TryGetValue(scopeKey, out var r) ? r : 0;
        }

        public void ClearAll()
        {
            _byScope.Clear();
            _scopeRevision.Clear();
        }

        private void BumpScopeRevision(string scopeKey)
        {
            if (string.IsNullOrEmpty(scopeKey)) return;
            _scopeRevision.TryGetValue(scopeKey, out int r);
            _scopeRevision[scopeKey] = r + 1;
        }

        // --------------------
        // Safety helpers
        // --------------------

        private static bool IsAlive(PersistentId id) => id != null;

        private static bool IsAlive(IPersistentEntity entity)
        {
            if (entity == null) return false;
            if (entity is UnityEngine.Object uo) return uo != null;
            return true;
        }

        private static string SafeName(PersistentId id)
        {
            if (id == null) return "(null/destroyed)";
            return id.name;
        }

        private static string SafeName(RegisteredEntity re)
        {
            if (re == null) return "(null)";
            if (re.Id == null) return "(id destroyed)";
            return re.Id.name;
        }

        private static string SafeScopeKey(PersistentId id)
        {
            if (id == null) return "";
            try { return id.ScopeKey ?? ""; }
            catch { return ""; }
        }
    }
}
