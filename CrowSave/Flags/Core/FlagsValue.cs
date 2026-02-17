using System;
using UnityEngine;

namespace CrowSave.Flags.Core
{
    public enum FlagsValueType
    {
        None = 0,
        Bool = 1,
        Int = 2,
        Float = 3,
        String = 4
    }

    [Serializable]
    public readonly struct FlagsValue : IEquatable<FlagsValue>
    {
        [SerializeField] private readonly FlagsValueType type;
        [SerializeField] private readonly bool boolValue;
        [SerializeField] private readonly int intValue;
        [SerializeField] private readonly float floatValue;
        [SerializeField] private readonly string stringValue;

        public FlagsValueType Type => type;

        public bool Bool => type == FlagsValueType.Bool ? boolValue : default;
        public int Int => type == FlagsValueType.Int ? intValue : default;
        public float Float => type == FlagsValueType.Float ? floatValue : default;
        public string String => type == FlagsValueType.String ? (stringValue ?? "") : "";

        private FlagsValue(FlagsValueType type, bool b, int i, float f, string s)
        {
            this.type = type;
            boolValue = b;
            intValue = i;
            floatValue = f;
            stringValue = s;
        }

        public static FlagsValue None => new FlagsValue(FlagsValueType.None, false, 0, 0f, "");

        public static FlagsValue FromBool(bool v) => new FlagsValue(FlagsValueType.Bool, v, 0, 0f, "");
        public static FlagsValue FromInt(int v) => new FlagsValue(FlagsValueType.Int, false, v, 0f, "");
        public static FlagsValue FromFloat(float v) => new FlagsValue(FlagsValueType.Float, false, 0, v, "");
        public static FlagsValue FromString(string v) => new FlagsValue(FlagsValueType.String, false, 0, 0f, v ?? "");

        public bool Equals(FlagsValue other)
        {
            if (type != other.type) return false;

            return type switch
            {
                FlagsValueType.None => true,
                FlagsValueType.Bool => boolValue == other.boolValue,
                FlagsValueType.Int => intValue == other.intValue,
                FlagsValueType.Float => Mathf.Approximately(floatValue, other.floatValue),
                FlagsValueType.String => string.Equals(stringValue ?? "", other.stringValue ?? "", StringComparison.Ordinal),
                _ => false
            };
        }

        public override bool Equals(object obj) => obj is FlagsValue v && Equals(v);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)type;
                h = (h * 397) ^ boolValue.GetHashCode();
                h = (h * 397) ^ intValue;
                h = (h * 397) ^ floatValue.GetHashCode();
                h = (h * 397) ^ (stringValue != null ? StringComparer.Ordinal.GetHashCode(stringValue) : 0);
                return h;
            }
        }

        public static bool operator ==(FlagsValue a, FlagsValue b) => a.Equals(b);
        public static bool operator !=(FlagsValue a, FlagsValue b) => !a.Equals(b);

        public override string ToString()
        {
            return type switch
            {
                FlagsValueType.None => "None",
                FlagsValueType.Bool => $"Bool({boolValue})",
                FlagsValueType.Int => $"Int({intValue})",
                FlagsValueType.Float => $"Float({floatValue})",
                FlagsValueType.String => $"String(\"{stringValue}\")",
                _ => "Unknown"
            };
        }
    }
}
