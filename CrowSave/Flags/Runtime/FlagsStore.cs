using System;
using System.Collections.Generic;
using CrowSave.Flags.Core;
using CrowSave.Persistence.Core;
using UnityEngine;

namespace CrowSave.Flags.Runtime
{
    // Internal entry with metadata. Minimal for now: value + revision.
    public struct FlagsEntry
    {
        public FlagsValue Value;
        public int Revision;

        public FlagsEntry(FlagsValue value, int revision)
        {
            Value = value;
            Revision = revision;
        }
    }

    /// <summary>
    /// Pure storage: scope -> target -> channel -> entry
    /// Deterministic capture (sorted).
    /// </summary>
    public sealed class FlagsStore
    {
        private const int BlobVersion = 1;

        // scopeKey -> targetKey -> channel -> entry
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, FlagsEntry>>> _data =
            new Dictionary<string, Dictionary<string, Dictionary<string, FlagsEntry>>>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, FlagsEntry>>> DataReadOnly => _data;

        public void ClearAll() => _data.Clear();

        public void ClearScope(string normalizedScopeKey)
        {
            normalizedScopeKey ??= "";
            if (normalizedScopeKey.Length == 0)
            {
                // global scope is still just a normal scope key in this model if you choose to store it as ""
                _data.Remove("");
                return;
            }

            _data.Remove(normalizedScopeKey);
        }

        public bool TryGet(string scopeKey, string targetKey, string channel, out FlagsEntry entry)
        {
            entry = default;

            scopeKey = FlagsScope.Normalize(scopeKey);
            targetKey = FlagsKeyUtil.Normalize(targetKey);
            channel = FlagsKeyUtil.NormalizeChannel(channel);

            if (targetKey.Length == 0 || channel.Length == 0) return false;

            if (!_data.TryGetValue(scopeKey, out var byTarget) || byTarget == null) return false;
            if (!byTarget.TryGetValue(targetKey, out var byChannel) || byChannel == null) return false;

            return byChannel.TryGetValue(channel, out entry);
        }

        public bool Set(string scopeKey, string targetKey, string channel, FlagsValue value, int revision, out bool hadOld, out FlagsEntry oldEntry)
        {
            hadOld = false;
            oldEntry = default;

            scopeKey = FlagsScope.Normalize(scopeKey);
            targetKey = FlagsKeyUtil.Normalize(targetKey);
            channel = FlagsKeyUtil.NormalizeChannel(channel);

            if (targetKey.Length == 0 || channel.Length == 0) return false;

            if (!_data.TryGetValue(scopeKey, out var byTarget) || byTarget == null)
            {
                byTarget = new Dictionary<string, Dictionary<string, FlagsEntry>>(StringComparer.Ordinal);
                _data[scopeKey] = byTarget;
            }

            if (!byTarget.TryGetValue(targetKey, out var byChannel) || byChannel == null)
            {
                byChannel = new Dictionary<string, FlagsEntry>(StringComparer.Ordinal);
                byTarget[targetKey] = byChannel;
            }

            hadOld = byChannel.TryGetValue(channel, out oldEntry);
            var newEntry = new FlagsEntry(value, revision);

            // No-op if same value and same revision is not important; value equality is enough to skip.
            if (hadOld && oldEntry.Value == value)
            {
                // Still allow revision update? Usually not needed.
                return false;
            }

            byChannel[channel] = newEntry;
            return true;
        }

        public bool Remove(string scopeKey, string targetKey, string channel, out bool hadOld, out FlagsEntry oldEntry)
        {
            hadOld = false;
            oldEntry = default;

            scopeKey = FlagsScope.Normalize(scopeKey);
            targetKey = FlagsKeyUtil.Normalize(targetKey);
            channel = FlagsKeyUtil.NormalizeChannel(channel);

            if (targetKey.Length == 0 || channel.Length == 0) return false;

            if (!_data.TryGetValue(scopeKey, out var byTarget) || byTarget == null) return false;
            if (!byTarget.TryGetValue(targetKey, out var byChannel) || byChannel == null) return false;

            hadOld = byChannel.TryGetValue(channel, out oldEntry);
            bool removed = byChannel.Remove(channel);

            if (removed && byChannel.Count == 0)
                byTarget.Remove(targetKey);

            if (removed && byTarget.Count == 0)
                _data.Remove(scopeKey);

            return removed;
        }

        // -------------------------
        // Serialization (CrowSave)
        // -------------------------

        public void Capture(IStateWriter w)
        {
            w.WriteVersion(BlobVersion);

            // Deterministic: sort scopes, targets, channels.
            var scopeKeys = new List<string>(_data.Keys);
            scopeKeys.Sort(StringComparer.Ordinal);

            w.WriteInt(scopeKeys.Count);

            for (int si = 0; si < scopeKeys.Count; si++)
            {
                string scope = scopeKeys[si] ?? "";
                w.WriteString(scope);

                var byTarget = _data[scope];
                if (byTarget == null)
                {
                    w.WriteInt(0);
                    continue;
                }

                var targetKeys = new List<string>(byTarget.Keys);
                targetKeys.Sort(StringComparer.Ordinal);

                w.WriteInt(targetKeys.Count);

                for (int ti = 0; ti < targetKeys.Count; ti++)
                {
                    string target = targetKeys[ti] ?? "";
                    w.WriteString(target);

                    var byChannel = byTarget[target];
                    if (byChannel == null)
                    {
                        w.WriteInt(0);
                        continue;
                    }

                    var channelKeys = new List<string>(byChannel.Keys);
                    channelKeys.Sort(StringComparer.Ordinal);

                    w.WriteInt(channelKeys.Count);

                    for (int ci = 0; ci < channelKeys.Count; ci++)
                    {
                        string channel = channelKeys[ci] ?? "";
                        var entry = byChannel[channel];

                        w.WriteString(channel);
                        w.WriteInt(entry.Revision);

                        WriteValue(w, entry.Value);
                    }
                }
            }
        }

        public void Apply(IStateReader r)
        {
            int v = r.ReadVersion(min: 1, max: 1000);

            _data.Clear();
            if (v != BlobVersion)
                return;

            int scopeCount = Mathf.Max(0, r.ReadInt());

            for (int si = 0; si < scopeCount; si++)
            {
                string scope = r.ReadString() ?? "";
                scope = FlagsScope.Normalize(scope);

                int targetCount = Mathf.Max(0, r.ReadInt());
                if (targetCount == 0) continue;

                var byTarget = new Dictionary<string, Dictionary<string, FlagsEntry>>(StringComparer.Ordinal);

                for (int ti = 0; ti < targetCount; ti++)
                {
                    string target = r.ReadString() ?? "";
                    target = FlagsKeyUtil.Normalize(target);

                    int channelCount = Mathf.Max(0, r.ReadInt());
                    if (channelCount == 0) continue;

                    var byChannel = new Dictionary<string, FlagsEntry>(StringComparer.Ordinal);

                    for (int ci = 0; ci < channelCount; ci++)
                    {
                        string channel = r.ReadString() ?? "";
                        channel = FlagsKeyUtil.NormalizeChannel(channel);

                        int rev = r.ReadInt();
                        var value = ReadValue(r);

                        if (target.Length != 0 && channel.Length != 0)
                            byChannel[channel] = new FlagsEntry(value, rev);
                    }

                    if (byChannel.Count > 0 && target.Length != 0)
                        byTarget[target] = byChannel;
                }

                if (byTarget.Count > 0)
                    _data[scope] = byTarget;
            }
        }

        private static void WriteValue(IStateWriter w, FlagsValue value)
        {
            w.WriteInt((int)value.Type);

            switch (value.Type)
            {
                case FlagsValueType.Bool: w.WriteInt(value.Bool ? 1 : 0); break;
                case FlagsValueType.Int: w.WriteInt(value.Int); break;
                case FlagsValueType.Float: w.WriteFloat(value.Float); break;
                case FlagsValueType.String: w.WriteString(value.String ?? ""); break;
                default: break;
            }
        }

        private static FlagsValue ReadValue(IStateReader r)
        {
            var t = (FlagsValueType)r.ReadInt();

            return t switch
            {
                FlagsValueType.Bool => FlagsValue.FromBool(r.ReadInt() != 0),
                FlagsValueType.Int => FlagsValue.FromInt(r.ReadInt()),
                FlagsValueType.Float => FlagsValue.FromFloat(r.ReadFloat()),
                FlagsValueType.String => FlagsValue.FromString(r.ReadString()),
                _ => FlagsValue.None
            };
        }
    }
}
