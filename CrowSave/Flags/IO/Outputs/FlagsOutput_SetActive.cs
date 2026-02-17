using System;
using System.Collections.Generic;
using CrowSave.Flags.Core;
using CrowSave.Flags.Runtime;
using UnityEngine;

namespace CrowSave.Flags.IO.Outputs
{
    [Serializable]
    public sealed class FlagsOutput_SetActive : FlagsOutputModule
    {
        [SerializeField] private FlagsTargetRef target;

        [Tooltip("When invoked, set active to this value and persist it.")]
        [SerializeField] private bool activeValue = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private static readonly List<GameObject> _tmp = new List<GameObject>(16);

        public override void Invoke(FlagsIOCause host, FlagsService flags, string scopeKey)
        {
            if (host == null || flags == null) return;

            FlagsTargetResolver.ResolveAll(target, _tmp);
            if (_tmp.Count == 0) return;

            for (int i = 0; i < _tmp.Count; i++)
            {
                var go = _tmp[i];
                if (go == null) continue;

                string targetKey = FlagsTargetResolver.GetTargetKey(go);
                if (string.IsNullOrWhiteSpace(targetKey)) continue;

                // Persist
                flags.SetBool(scopeKey, targetKey, FlagsChannel.Active, activeValue);

                // Apply now
                go.SetActive(activeValue);

                if (debugLogs)
                    Debug.Log($"[CrowSave.Flags][SetActive][Invoke] scope='{scopeKey}' target='{targetKey}' active={activeValue}", host);
            }
        }

        public override void Restore(FlagsIOCause host, FlagsService flags, string scopeKey)
        {
            if (host == null || flags == null) return;

            FlagsTargetResolver.ResolveAll(target, _tmp);
            if (_tmp.Count == 0) return;

            for (int i = 0; i < _tmp.Count; i++)
            {
                var go = _tmp[i];
                if (go == null) continue;

                string targetKey = FlagsTargetResolver.GetTargetKey(go);
                if (string.IsNullOrWhiteSpace(targetKey)) continue;

                // Only apply if a stored value exists
                if (!flags.TryGet(scopeKey, targetKey, FlagsChannel.Active, out var entry))
                    continue;

                if (entry.Value.Type != FlagsValueType.Bool)
                    continue;

                bool v = entry.Value.Bool;
                go.SetActive(v);

                if (debugLogs)
                    Debug.Log($"[CrowSave.Flags][SetActive][Restore] scope='{scopeKey}' target='{targetKey}' active={v}", host);
            }
        }
    }
}
