using System;

namespace CrowSave.Persistence.Reflect
{
    /// <summary>
    /// Opt-in marker for fields/properties, and optional stable key override.
    /// If Key is provided, it becomes the stable persistence key (hash input).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class PersistAttribute : Attribute
    {
        public PersistAttribute() { }
        public PersistAttribute(string key) { Key = key; }
        public string Key { get; }
    }

    /// <summary>
    /// Explicit stable key override. Prefer when you want renames/moves not to break saves.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class PersistKeyAttribute : Attribute
    {
        public PersistKeyAttribute(string key) { Key = key ?? ""; }
        public string Key { get; }
    }

    /// <summary>
    /// Explicit opt-out (useful if you later add broader inclusion rules).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class PersistIgnoreAttribute : Attribute { }
}
