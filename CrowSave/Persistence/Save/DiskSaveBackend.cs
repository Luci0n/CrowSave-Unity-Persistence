using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CrowSave.Persistence.Save
{
    public sealed class DiskSaveBackend
    {
        private readonly string _rootDir;

        public DiskSaveBackend(string folderName = "saves")
        {
            _rootDir = Path.Combine(Application.persistentDataPath, folderName);
            Directory.CreateDirectory(_rootDir);
        }

        private string SlotPath(int slot) => Path.Combine(_rootDir, $"slot_{slot:D2}.sav");
        private string TempPath(int slot) => Path.Combine(_rootDir, $"slot_{slot:D2}.tmp");
        private string BakPath(int slot)  => Path.Combine(_rootDir, $"slot_{slot:D2}.bak");

        public bool Exists(int slot) => File.Exists(SlotPath(slot));

        public void WriteAtomic(int slot, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var final = SlotPath(slot);
            var tmp   = TempPath(slot);
            var bak   = BakPath(slot);

            Directory.CreateDirectory(_rootDir);

            // Clean temp from any previous failed attempt.
            if (File.Exists(tmp)) File.Delete(tmp);

            try
            {
                WriteAllBytesAndFlushToDisk(tmp, data);

                if (!File.Exists(final))
                {
                    File.Move(tmp, final);
                    return;
                }

                // Best-effort: atomic replace + backup (where supported)
                if (TryFileReplace(tmp, final, bak))
                {
                    return;
                }

                // Fallback: manual backup + move
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(final, bak);
                File.Move(tmp, final);
            }
            finally
            {
                if (File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch { /* ignore */ }
                }
            }
        }

        public void Delete(int slot)
        {
            var final = SlotPath(slot);
            var tmp = TempPath(slot);
            var bak = BakPath(slot);

            if (File.Exists(final)) File.Delete(final);
            if (File.Exists(tmp)) File.Delete(tmp);
            if (File.Exists(bak)) File.Delete(bak);
        }

        public byte[] Read(int slot)
        {
            var final = SlotPath(slot);
            if (!File.Exists(final)) return null;
            return File.ReadAllBytes(final);
        }

        public byte[] ReadBackup(int slot)
        {
            var bak = BakPath(slot);
            if (!File.Exists(bak)) return null;
            return File.ReadAllBytes(bak);
        }

        private static void WriteAllBytesAndFlushToDisk(string path, byte[] data)
        {
            // Try write-through first; if unsupported on platform/filesystem, fall back.
            try
            {
                using (var fs = new FileStream(
                           path,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None,
                           bufferSize: 64 * 1024,
                           options: FileOptions.WriteThrough))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush(true); // flush OS buffers best-effort
                }
            }
            catch
            {
                using (var fs = new FileStream(
                           path,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None,
                           bufferSize: 64 * 1024,
                           options: FileOptions.None))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush(true);
                }
            }
        }

        private static bool TryFileReplace(string sourceTmp, string destFinal, string destBackup)
        {
            try
            {
                // Prefer 4-arg overload if present: Replace(src, dst, bak, ignoreMetadataErrors)
                var mi = typeof(File).GetMethod(
                    "Replace",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(string), typeof(string), typeof(string), typeof(bool) },
                    modifiers: null);

                if (mi != null)
                {
                    mi.Invoke(null, new object[] { sourceTmp, destFinal, destBackup, true });
                    return true;
                }

                // Fallback to 3-arg overload if present.
                File.Replace(sourceTmp, destFinal, destBackup);
                return true;
            }
            catch
            {
                // Platform not supported, permission issue, cross-volume move, etc.
                return false;
            }
        }
    }
}
