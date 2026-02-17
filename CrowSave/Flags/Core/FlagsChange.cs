using System;

namespace CrowSave.Flags.Core
{
    public readonly struct FlagsChange
    {
        public readonly string ScopeKey;
        public readonly string TargetKey;
        public readonly string Channel;

        public readonly bool HadOld;
        public readonly FlagsValue OldValue;

        public readonly FlagsValue NewValue;

        public FlagsChange(
            string scopeKey,
            string targetKey,
            string channel,
            bool hadOld,
            FlagsValue oldValue,
            FlagsValue newValue)
        {
            ScopeKey = scopeKey ?? "";
            TargetKey = targetKey ?? "";
            Channel = channel ?? "";
            HadOld = hadOld;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public override string ToString()
            => $"FlagsChange(scope='{ScopeKey}', target='{TargetKey}', channel='{Channel}', old={(HadOld ? OldValue.ToString() : "<none>")}, new={NewValue})";
    }
}
