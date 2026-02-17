using System;
using System.IO;
using System.Text;
using CrowSave.Persistence.Runtime;

namespace CrowSave.Persistence.Save
{
    public static class SaveSerializer
    {
        // v4: splits scene into ActiveSceneId + ActiveSceneLoad (and keeps prior v3 metadata)
        private const int CurrentVersion = 4;

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("GPERSIST");

        // Hard caps (tune as needed)
        private const int MaxBlobBytes = 64 * 1024 * 1024;     // 64 MB per blob (global or entity)
        private const int MaxStringBytes = 1 * 1024 * 1024;    // 1 MB per string
        private const int MaxScopes = 2048;
        private const int MaxDestroyedPerScope = 20000;
        private const int MaxEntitiesPerScope = 20000;

        public static byte[] Serialize(SavePackage pkg)
        {
            if (pkg == null) throw new ArgumentNullException(nameof(pkg));

            using var ms = new MemoryStream(32 * 1024);
            using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            // Header
            bw.Write(Magic);
            bw.Write(CurrentVersion);

            // v4 metadata
            bw.Write(pkg.ActiveSceneId ?? "");
            bw.Write(pkg.ActiveSceneLoad ?? "");
            bw.Write(pkg.SavedUtcTicks);

            // v3 metadata (kept)
            bw.Write((byte)pkg.Kind);
            bw.Write(pkg.Slot);
            bw.Write(pkg.Note ?? "");

            // Global blob
            WriteBytes(bw, pkg.GlobalStateBlob, "GlobalStateBlob");

            // Scopes
            bw.Write(pkg.Scopes.Count);
            for (int i = 0; i < pkg.Scopes.Count; i++)
            {
                var s = pkg.Scopes[i];
                bw.Write(s.ScopeKey ?? "");

                // Destroyed
                bw.Write(s.Destroyed.Count);
                for (int d = 0; d < s.Destroyed.Count; d++)
                    bw.Write(s.Destroyed[d] ?? "");

                // Entities
                bw.Write(s.Entities.Count);
                for (int e = 0; e < s.Entities.Count; e++)
                {
                    var er = s.Entities[e];
                    bw.Write(er.EntityId ?? "");
                    WriteBytes(bw, er.Blob, $"EntityBlob({er.EntityId ?? "?"})");
                }
            }

            bw.Flush();
            return ms.ToArray();
        }

        public static SavePackage Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Empty save data.");

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            // Header (exact)
            var magic = ReadBytesExact(br, Magic.Length, "magic");
            if (!BytesEqual(magic, Magic))
                throw new InvalidDataException("Invalid save magic (file not recognized).");

            int version = br.ReadInt32();
            if (version != 1 && version != 2 && version != 3 && version != 4)
                throw new InvalidDataException($"Unsupported save version: {version}");

            var pkg = new SavePackage { Version = version };

            // Metadata
            if (version >= 4)
            {
                pkg.ActiveSceneId = ReadStringCapped(br, MaxStringBytes, "ActiveSceneId");
                pkg.ActiveSceneLoad = ReadStringCapped(br, MaxStringBytes, "ActiveSceneLoad");
                pkg.SavedUtcTicks = br.ReadInt64();

                // v3+
                pkg.Kind = (SaveKind)br.ReadByte();
                pkg.Slot = br.ReadInt32();
                pkg.Note = ReadStringCapped(br, MaxStringBytes, "Note");
            }
            else
            {
                // v2/v3: ActiveScene (name) + ticks
                // v1: no metadata
                string legacyScene = "";
                long ticks = 0;

                if (version >= 2)
                {
                    legacyScene = ReadStringCapped(br, MaxStringBytes, "LegacyScene");
                    ticks = br.ReadInt64();
                }

                if (version >= 3)
                {
                    pkg.Kind = (SaveKind)br.ReadByte();
                    pkg.Slot = br.ReadInt32();
                    pkg.Note = ReadStringCapped(br, MaxStringBytes, "Note");
                }
                else
                {
                    pkg.Kind = SaveKind.Unknown;
                    pkg.Slot = -1;
                    pkg.Note = "";
                }

                pkg.SavedUtcTicks = ticks;
                pkg.ActiveSceneLoad = legacyScene ?? "";
                pkg.ActiveSceneId = SceneIdentity.NormalizeLegacy(legacyScene ?? "");
            }

            // Global blob (capped + exact)
            pkg.GlobalStateBlob = ReadBytesCapped(br, MaxBlobBytes, "GlobalStateBlob");

            // Scopes (count-capped)
            int scopeCount = ReadCount(br, MaxScopes, "scope");
            if (pkg.Scopes != null) pkg.Scopes.Capacity = Math.Max(pkg.Scopes.Capacity, scopeCount);

            for (int i = 0; i < scopeCount; i++)
            {
                var s = new SavePackage.ScopeRecord
                {
                    ScopeKey = ReadStringCapped(br, MaxStringBytes, "ScopeKey")
                };

                int destroyedCount = ReadCount(br, MaxDestroyedPerScope, $"destroyed (scope '{s.ScopeKey ?? ""}')");
                if (s.Destroyed != null) s.Destroyed.Capacity = Math.Max(s.Destroyed.Capacity, destroyedCount);

                for (int d = 0; d < destroyedCount; d++)
                    s.Destroyed.Add(ReadStringCapped(br, MaxStringBytes, "DestroyedId"));

                int entityCount = ReadCount(br, MaxEntitiesPerScope, $"entity (scope '{s.ScopeKey ?? ""}')");
                if (s.Entities != null) s.Entities.Capacity = Math.Max(s.Entities.Capacity, entityCount);

                for (int e = 0; e < entityCount; e++)
                {
                    var er = new SavePackage.EntityRecord
                    {
                        EntityId = ReadStringCapped(br, MaxStringBytes, "EntityId"),
                        Blob = ReadBytesCapped(br, MaxBlobBytes, "EntityBlob")
                    };
                    s.Entities.Add(er);
                }

                pkg.Scopes.Add(s);
            }

            // Ensure v4 fields exist even if empty
            pkg.ActiveSceneId = SceneIdentity.NormalizeLegacy(pkg.ActiveSceneId);
            pkg.ActiveSceneLoad ??= "";

            return pkg;
        }

        private static void WriteBytes(BinaryWriter bw, byte[] bytes, string label)
        {
            if (bytes == null) { bw.Write(-1); return; }

            if (bytes.Length > MaxBlobBytes)
                throw new InvalidDataException($"SaveSerializer: refusing to write {label} of size {bytes.Length} (> cap {MaxBlobBytes}).");

            bw.Write(bytes.Length);
            bw.Write(bytes);
        }

        private static int ReadCount(BinaryReader br, int max, string label)
        {
            int count = br.ReadInt32();
            if (count < 0 || count > max)
                throw new InvalidDataException($"Invalid {label} count: {count} (max {max}).");
            return count;
        }

        private static byte[] ReadBytesCapped(BinaryReader br, int maxBytes, string label)
        {
            int len = br.ReadInt32();
            if (len < 0) return null;

            if (len > maxBytes)
                throw new InvalidDataException($"Invalid {label} length: {len} (max {maxBytes}).");

            if (br.BaseStream.CanSeek)
            {
                long remaining = br.BaseStream.Length - br.BaseStream.Position;
                if (len > remaining)
                    throw new InvalidDataException($"Truncated save while reading {label}: len {len} > remaining {remaining}.");
            }

            var bytes = br.ReadBytes(len);
            if (bytes.Length != len)
                throw new InvalidDataException($"Truncated save while reading {label}: expected {len} bytes, got {bytes.Length}.");

            return bytes;
        }

        private static byte[] ReadBytesExact(BinaryReader br, int len, string label)
        {
            if (len < 0) throw new InvalidDataException($"Invalid {label} length: {len}.");

            if (br.BaseStream.CanSeek)
            {
                long remaining = br.BaseStream.Length - br.BaseStream.Position;
                if (len > remaining)
                    throw new InvalidDataException($"Truncated save while reading {label}: need {len}, remaining {remaining}.");
            }

            var bytes = br.ReadBytes(len);
            if (bytes.Length != len)
                throw new InvalidDataException($"Truncated save while reading {label}: expected {len}, got {bytes.Length}.");

            return bytes;
        }

        private static string ReadStringCapped(BinaryReader br, int maxBytes, string label)
        {
            int byteLen = Read7BitEncodedInt(br);

            if (byteLen < 0)
                throw new InvalidDataException($"Invalid {label} length: {byteLen}.");

            if (byteLen > maxBytes)
                throw new InvalidDataException($"{label} length {byteLen} exceeds cap {maxBytes}.");

            if (br.BaseStream.CanSeek)
            {
                long remaining = br.BaseStream.Length - br.BaseStream.Position;
                if (byteLen > remaining)
                    throw new InvalidDataException($"Truncated save while reading {label}: need {byteLen}, remaining {remaining}.");
            }

            if (byteLen == 0) return "";

            var bytes = br.ReadBytes(byteLen);
            if (bytes.Length != byteLen)
                throw new InvalidDataException($"Truncated save while reading {label}: expected {byteLen}, got {bytes.Length}.");

            return Encoding.UTF8.GetString(bytes);
        }

        private static int Read7BitEncodedInt(BinaryReader br)
        {
            int count = 0;
            int shift = 0;

            while (shift != 35)
            {
                int b = br.ReadByte();
                count |= (b & 0x7F) << shift;
                shift += 7;

                if ((b & 0x80) == 0)
                    return count;
            }

            throw new InvalidDataException("Invalid 7-bit encoded int (too many bytes).");
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
