using System.Collections.Generic;

namespace CrowSave.Persistence.Save
{
    public sealed class SavePackage
    {
        public int Version = 4;

        // v4+: scene identity is split:
        // - ActiveSceneId: typed identifier used as scope key (name:/path:/guid:)
        // - ActiveSceneLoad: string used to load the scene (usually name, optionally path)
        public string ActiveSceneId;
        public string ActiveSceneLoad;
        public long SavedUtcTicks;
        public SaveKind Kind = SaveKind.Unknown;
        public int Slot = -1;
        public string Note; // optional label like "hotkey", "transition"

        public byte[] GlobalStateBlob;

        public readonly List<ScopeRecord> Scopes = new List<ScopeRecord>();

        public sealed class ScopeRecord
        {
            public string ScopeKey;

            public readonly List<string> Destroyed = new List<string>();
            public readonly List<EntityRecord> Entities = new List<EntityRecord>();
        }

        public sealed class EntityRecord
        {
            public string EntityId;
            public byte[] Blob;
        }
    }
}
