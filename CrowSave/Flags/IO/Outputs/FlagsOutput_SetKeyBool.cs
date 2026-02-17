using System;
using CrowSave.Flags.Core;
using CrowSave.Flags.Runtime;
using UnityEngine;

namespace CrowSave.Flags.IO.Outputs
{
    [Serializable]
    public sealed class FlagsOutput_SetKeyBool : FlagsOutputModule
    {
        [Header("Custom Flag Key")]
        [Tooltip("Example: crate:special:disabled")]
        [SerializeField] private string flagKey = "example:flag";

        [Tooltip("Channel under that key. Usually 'value' or something semantic.")]
        [SerializeField] private string channel = "value";

        [SerializeField] private bool value = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        public override void Invoke(FlagsIOCause host, FlagsService flags, string scopeKey)
        {
            if (flags == null) return;

            string k = FlagsKeyUtil.Normalize(flagKey);
            string ch = FlagsKeyUtil.NormalizeChannel(channel);

            if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(ch))
                return;

            // Store as a virtual target: "key:<flagKey>"
            string targetKey = $"key:{k}";
            flags.SetBool(scopeKey, targetKey, ch, value);

            if (debugLogs)
                Debug.Log($"[CrowSave.Flags][SetKeyBool][Invoke] scope='{scopeKey}' target='{targetKey}' channel='{ch}' = {value}", host);
        }

        public override void Restore(FlagsIOCause host, FlagsService flags, string scopeKey)
        {

        }
    }
}
