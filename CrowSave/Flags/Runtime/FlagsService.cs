using System;
using CrowSave.Flags.Core;
using System.Collections.Generic;

namespace CrowSave.Flags.Runtime
{
    /// <summary>
    /// Runtime API facade over FlagsStore. Game/IO code talks to this.
    /// Writes call markDirty so DirtyOnly capture works under CrowSave.
    /// </summary>
    public sealed class FlagsService
    {
        private readonly FlagsStore _store;
        private readonly Action _markDirty;

        public event Action<FlagsChange> StateChanged;
        public event Action StateRebuilt;

        public int Revision => _revision;
        private int _revision;

        public FlagsService(FlagsStore store, Action markDirty)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _markDirty = markDirty;
        }

        public bool TryGet(string scopeKey, string targetKey, string channel, out FlagsEntry entry)
            => _store.TryGet(scopeKey, targetKey, channel, out entry);

        public bool GetBool(string scopeKey, string targetKey, string channel, bool fallback = false)
        {
            if (_store.TryGet(scopeKey, targetKey, channel, out var e) && e.Value.Type == FlagsValueType.Bool)
                return e.Value.Bool;
            return fallback;
        }

        public int GetInt(string scopeKey, string targetKey, string channel, int fallback = 0)
        {
            if (_store.TryGet(scopeKey, targetKey, channel, out var e) && e.Value.Type == FlagsValueType.Int)
                return e.Value.Int;
            return fallback;
        }

        public float GetFloat(string scopeKey, string targetKey, string channel, float fallback = 0f)
        {
            if (_store.TryGet(scopeKey, targetKey, channel, out var e) && e.Value.Type == FlagsValueType.Float)
                return e.Value.Float;
            return fallback;
        }

        public string GetString(string scopeKey, string targetKey, string channel, string fallback = "")
        {
            if (_store.TryGet(scopeKey, targetKey, channel, out var e) && e.Value.Type == FlagsValueType.String)
                return e.Value.String ?? "";
            return fallback ?? "";
        }

        public void SetBool(string scopeKey, string targetKey, string channel, bool v)
            => Set(scopeKey, targetKey, channel, FlagsValue.FromBool(v));

        public void SetInt(string scopeKey, string targetKey, string channel, int v)
            => Set(scopeKey, targetKey, channel, FlagsValue.FromInt(v));

        public void SetFloat(string scopeKey, string targetKey, string channel, float v)
            => Set(scopeKey, targetKey, channel, FlagsValue.FromFloat(v));

        public void SetString(string scopeKey, string targetKey, string channel, string v)
            => Set(scopeKey, targetKey, channel, FlagsValue.FromString(v));

        public bool Remove(string scopeKey, string targetKey, string channel)
        {
            bool removed = _store.Remove(scopeKey, targetKey, channel, out bool hadOld, out var oldEntry);
            if (!removed) return false;

            _markDirty?.Invoke();
            _revision++;

            StateChanged?.Invoke(new FlagsChange(
                scopeKey, targetKey, channel,
                hadOld, hadOld ? oldEntry.Value : FlagsValue.None,
                FlagsValue.None
            ));

            return true;
        }

        public void ClearAll()
        {
            _store.ClearAll();
            _markDirty?.Invoke();
            _revision++;
            StateRebuilt?.Invoke();
        }

        public void ClearScope(string scopeKey)
        {
            _store.ClearScope(scopeKey);
            _markDirty?.Invoke();
            _revision++;
            StateRebuilt?.Invoke();
        }

        public void NotifyRebuilt()
        {
            _revision++;
            StateRebuilt?.Invoke();
        }

        private void Set(string scopeKey, string targetKey, string channel, FlagsValue value)
        {
            // Revision increments per-change; we also store revision into the entry for deterministic last-write-wins.
            int newRev = _revision + 1;

            bool changed = _store.Set(scopeKey, targetKey, channel, value, newRev, out bool hadOld, out var oldEntry);
            if (!changed) return;

            _markDirty?.Invoke();
            _revision = newRev;

            StateChanged?.Invoke(new FlagsChange(
                scopeKey, targetKey, channel,
                hadOld, hadOld ? oldEntry.Value : FlagsValue.None,
                value
            ));
        }

        public void GetSnapshot(List<(string scope, string target, string channel, FlagsValue value, int revision)> outRows)
        {
            if (outRows == null) return;
            outRows.Clear();

            foreach (var scopePair in _store.DataReadOnly)
            {
                string scope = scopePair.Key ?? "";
                var byTarget = scopePair.Value;
                if (byTarget == null) continue;

                foreach (var targetPair in byTarget)
                {
                    string target = targetPair.Key ?? "";
                    var byChannel = targetPair.Value;
                    if (byChannel == null) continue;

                    foreach (var chanPair in byChannel)
                    {
                        string channel = chanPair.Key ?? "";
                        var entry = chanPair.Value;

                        outRows.Add((scope, target, channel, entry.Value, entry.Revision));
                    }
                }
            }
        }
    }
}
