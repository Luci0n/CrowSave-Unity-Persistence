using System;
using System.IO;
using System.Text;
using CrowSave.Persistence.Runtime;

namespace CrowSave.Persistence.Save
{
    /// <summary>
    /// Reads only the save header/metadata without deserializing full scope/entity blobs.
    /// Must match SaveSerializer format.
    /// </summary>
    public static class SaveHeaderReader
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("GPERSIST");

        public readonly struct Header
        {
            public readonly bool IsValid;
            public readonly int Version;

            // v4+
            public readonly string ActiveSceneId;
            public readonly string ActiveSceneLoad;

            // v2/v3 legacy
            public readonly string LegacyActiveScene;

            public readonly long SavedUtcTicks;

            // v3+
            public readonly SaveKind Kind;
            public readonly int Slot;
            public readonly string Note;

            public readonly int TotalBytes;

            public Header(
                bool isValid,
                int version,
                string activeSceneId,
                string activeSceneLoad,
                string legacyActiveScene,
                long savedUtcTicks,
                SaveKind kind,
                int slot,
                string note,
                int totalBytes)
            {
                IsValid = isValid;
                Version = version;

                ActiveSceneId = activeSceneId;
                ActiveSceneLoad = activeSceneLoad;
                LegacyActiveScene = legacyActiveScene;

                SavedUtcTicks = savedUtcTicks;

                Kind = kind;
                Slot = slot;
                Note = note;

                TotalBytes = totalBytes;
            }
        }

        public static Header TryReadHeader(byte[] data)
        {
            if (data == null || data.Length == 0)
                return new Header(false, 0, null, null, null, 0, SaveKind.Unknown, -1, null, 0);

            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

                var magic = br.ReadBytes(Magic.Length);
                if (!BytesEqual(magic, Magic))
                    return new Header(false, 0, null, null, null, 0, SaveKind.Unknown, -1, null, data.Length);

                int version = br.ReadInt32();
                if (version != 1 && version != 2 && version != 3 && version != 4)
                    return new Header(false, version, null, null, null, 0, SaveKind.Unknown, -1, null, data.Length);

                string activeSceneId = "";
                string activeSceneLoad = "";
                string legacyScene = "";
                long ticks = 0;

                SaveKind kind = SaveKind.Unknown;
                int slot = -1;
                string note = "";

                if (version >= 4)
                {
                    activeSceneId = SceneIdentity.NormalizeLegacy(br.ReadString());
                    activeSceneLoad = br.ReadString();
                    ticks = br.ReadInt64();

                    kind = (SaveKind)br.ReadByte();
                    slot = br.ReadInt32();
                    note = br.ReadString();
                }
                else
                {
                    // v2/v3 metadata
                    if (version >= 2)
                    {
                        legacyScene = br.ReadString();
                        ticks = br.ReadInt64();
                    }

                    if (version >= 3)
                    {
                        kind = (SaveKind)br.ReadByte();
                        slot = br.ReadInt32();
                        note = br.ReadString();
                    }

                    activeSceneLoad = legacyScene ?? "";
                    activeSceneId = SceneIdentity.NormalizeLegacy(legacyScene ?? "");
                }

                return new Header(true, version, activeSceneId, activeSceneLoad, legacyScene, ticks, kind, slot, note, data.Length);
            }
            catch
            {
                return new Header(false, 0, null, null, null, 0, SaveKind.Unknown, -1, null, data.Length);
            }
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
