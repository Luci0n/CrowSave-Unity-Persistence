using System.Collections.Generic;

namespace CrowSave.Persistence.Runtime
{
    public sealed class ScopeState
    {
        public readonly HashSet<string> Destroyed = new HashSet<string>();
        public readonly Dictionary<string, byte[]> EntityBlobs = new Dictionary<string, byte[]>();
        public readonly HashSet<string> DiskEligible = new HashSet<string>();

        public int Revision { get; private set; } = 0;
        public void BumpRevision() => Revision++;
    }

    public sealed class WorldStateRAM
    {
        public byte[] GlobalStateBlob;

        private readonly Dictionary<string, ScopeState> _scopes = new Dictionary<string, ScopeState>();
        public IReadOnlyDictionary<string, ScopeState> Scopes => _scopes;

        public ScopeState GetOrCreate(string scopeKey)
        {
            if (!_scopes.TryGetValue(scopeKey, out var scope))
            {
                scope = new ScopeState();
                _scopes[scopeKey] = scope;
            }
            return scope;
        }

        public bool TryGet(string scopeKey, out ScopeState scope) => _scopes.TryGetValue(scopeKey, out scope);

        public void ClearScope(string scopeKey) => _scopes.Remove(scopeKey);
        public void ClearAll() => _scopes.Clear();

        /// <summary>
        /// Renames a scope key in RAM (used when loaded saves use an older scene id format).
        /// Returns true if a rename happened.
        /// </summary>
        public bool TryRenameScope(string fromScopeKey, string toScopeKey, bool overwriteDestination = false)
        {
            if (string.IsNullOrWhiteSpace(fromScopeKey)) return false;
            if (string.IsNullOrWhiteSpace(toScopeKey)) return false;
            if (string.Equals(fromScopeKey, toScopeKey, System.StringComparison.Ordinal)) return false;

            if (!_scopes.TryGetValue(fromScopeKey, out var scope))
                return false;

            if (_scopes.ContainsKey(toScopeKey))
            {
                if (!overwriteDestination)
                    return false;

                _scopes.Remove(toScopeKey);
            }

            _scopes.Remove(fromScopeKey);
            _scopes[toScopeKey] = scope;

            scope.BumpRevision();
            return true;
        }
    }
}
