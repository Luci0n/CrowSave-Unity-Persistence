using System;

namespace CrowSave.Persistence.Core
{
    [Serializable]
    public readonly struct EntityKey : IEquatable<EntityKey>
    {
        public readonly string ScopeKey;   // scene/cell id
        public readonly string EntityId;   // persistent guid string

        public EntityKey(string scopeKey, string entityId)
        {
            ScopeKey = scopeKey ?? "";
            EntityId = entityId ?? "";
        }

        public bool Equals(EntityKey other) => ScopeKey == other.ScopeKey && EntityId == other.EntityId;
        public override bool Equals(object obj) => obj is EntityKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ScopeKey, EntityId);
        public override string ToString() => $"{ScopeKey}:{EntityId}";
    }
}
