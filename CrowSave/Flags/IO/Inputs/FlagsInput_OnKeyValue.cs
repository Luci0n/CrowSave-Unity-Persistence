using System;
using CrowSave.Flags.Core;
using CrowSave.Flags.Runtime;
using UnityEngine;

namespace CrowSave.Flags.IO.Inputs
{
    [Serializable]
    public sealed class FlagsInput_OnKeyValue : FlagsInputModule
    {
        public enum CompareMode
        {
            Bool = 0,
            Int = 1,
            Float = 2,
            String = 3
        }

        [Header("Key")]
        [Tooltip("Example: crate:special:disabled")]
        [SerializeField] private string flagKey = "example:flag";

        [Tooltip("Channel under that key. Usually 'value' or something semantic.")]
        [SerializeField] private string channel = "value";

        [Header("Compare")]
        [SerializeField] private CompareMode compareMode = CompareMode.Bool;

        [SerializeField] private bool boolValue = true;
        [SerializeField] private int intValue = 1;
        [SerializeField] private float floatValue = 1f;
        [SerializeField] private string stringValue = "1";

        [Tooltip("Only used for Float compare.")]
        [SerializeField, Min(0f)] private float floatEpsilon = 0.0001f;

        [Header("Timing")]
        [Tooltip("If 0, checks every Update(). If > 0, checks on a timer.")]
        [SerializeField, Min(0f)] private float pollIntervalSeconds = 0.25f;

        [Tooltip("If true, fires when value becomes equal (edge). If false, fires every check while equal (FireOnce can still prevent repeats).")]
        [SerializeField] private bool edgeTriggered = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        [NonSerialized] private float _nextPollTime;
        [NonSerialized] private bool _wasMatch;

        public override void OnEnable(FlagsIOCause host)
        {
            _nextPollTime = 0f;
            _wasMatch = false;
        }

        public override void OnUpdate(FlagsIOCause host)
        {
            if (host == null) return;

            if (pollIntervalSeconds > 0f)
            {
                if (Time.unscaledTime < _nextPollTime) return;
                _nextPollTime = Time.unscaledTime + pollIntervalSeconds;
            }

            Evaluate(host);
        }

        private void Evaluate(FlagsIOCause host)
        {
            if (!host.EnsureBound())
                return;

            var flags = host.Flags;
            if (flags == null) return;

            string k = FlagsKeyUtil.Normalize(flagKey);
            string ch = FlagsKeyUtil.NormalizeChannel(channel);

            if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(ch))
                return;

            string scopeKey = host.GetScopeKey();
            string targetKey = $"key:{k}";

            bool match = ReadAndCompare(flags, scopeKey, targetKey, ch);

            if (edgeTriggered)
            {
                if (match && !_wasMatch)
                {
                    if (debugLogs)
                        Debug.Log($"[CrowSave.Flags][OnKeyValue] EDGE FIRE scope='{scopeKey}' target='{targetKey}' channel='{ch}'", host);

                    Fire(host);
                }
            }
            else
            {
                if (match)
                {
                    if (debugLogs)
                        Debug.Log($"[CrowSave.Flags][OnKeyValue] FIRE scope='{scopeKey}' target='{targetKey}' channel='{ch}'", host);

                    Fire(host);
                }
            }

            _wasMatch = match;
        }

        private bool ReadAndCompare(FlagsService flags, string scopeKey, string targetKey, string channel)
        {
            switch (compareMode)
            {
                case CompareMode.Bool:
                    return flags.GetBool(scopeKey, targetKey, channel, false) == boolValue;

                case CompareMode.Int:
                    return flags.GetInt(scopeKey, targetKey, channel, 0) == intValue;

                case CompareMode.Float:
                {
                    float v = flags.GetFloat(scopeKey, targetKey, channel, 0f);
                    return Mathf.Abs(v - floatValue) <= floatEpsilon;
                }

                case CompareMode.String:
                default:
                    return string.Equals(flags.GetString(scopeKey, targetKey, channel, ""), stringValue ?? "", StringComparison.Ordinal);
            }
        }
    }
}
