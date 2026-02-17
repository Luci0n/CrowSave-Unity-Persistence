using System;
using System.IO;
using CrowSave.Persistence.Runtime;
using UnityEngine.SceneManagement;

namespace CrowSave.Persistence.Save
{
    public sealed class SaveManager
    {
        private readonly DiskSaveBackend _backend;

        // Set by SaveOrchestrator right before writing a slot.
        // v4+: ActiveSceneId = persistence scope key (typed: name:/path:/guid:)
        //      ActiveSceneLoad = what we actually load (typed key or legacy name)
        private string _nextActiveSceneId = "";
        private string _nextActiveSceneLoad = "";

        // metadata set by orchestrator (string -> parsed into SaveKind safely)
        private string _nextKindName = null;
        private string _nextNote = null;

        public SavePackage LastLoadedPackage { get; private set; }

        public SaveManager(DiskSaveBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        /// Backwards-compatible overload (kind/note default).
        public void SetNextSaveHeader(string activeSceneId, string activeSceneLoad)
            => SetNextSaveHeader(activeSceneId, activeSceneLoad, kindName: null, note: null);

        /// SaveOrchestrator calls this before SaveSlotFromRAM so the save header + metadata match intent.
        public void SetNextSaveHeader(string activeSceneId, string activeSceneLoad, string kindName, string note)
        {
            _nextActiveSceneId = activeSceneId ?? "";
            _nextActiveSceneLoad = activeSceneLoad ?? "";
            _nextKindName = kindName;
            _nextNote = note;
        }

        /// NEVER throws (disk IO failures are handled + logged).
        public void SaveSlotFromRAM(int slot)
        {
            try
            {
                var pkg = BuildPackageFromRAM(_nextActiveSceneId, _nextActiveSceneLoad);

                // Stamp metadata
                pkg.Slot = slot;
                pkg.Note = _nextNote;
                pkg.Kind = ParseKindOrUnknown(_nextKindName);

                var bytes = SaveSerializer.Serialize(pkg);

                // Disk write can throw (full disk, permission, lock, etc.)
                _backend.WriteAtomic(slot, bytes);

                PersistenceLog.Info(
                    $"Saved slot {slot} ({bytes.Length} bytes) kind={pkg.Kind} note='{pkg.Note ?? ""}' sceneId='{pkg.ActiveSceneId}' load='{pkg.ActiveSceneLoad}'"
                );
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is InvalidDataException ||
                ex is ArgumentException ||
                ex is NotSupportedException)
            {
                PersistenceLog.Error($"SAVE failed slot {slot}: {ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                PersistenceLog.Error($"SAVE failed slot {slot} (unexpected): {ex}");
            }
            finally
            {
                _nextKindName = null;
                _nextNote = null;
            }
        }

        /// NEVER throws (disk IO failures are handled + logged).
        public bool LoadSlotToRAM(int slot)
        {
            LastLoadedPackage = null;

            byte[] bytes = null;
            try
            {
                bytes = _backend.Read(slot);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is NotSupportedException)
            {
                PersistenceLog.Error($"LOAD failed slot {slot}: disk read error: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                PersistenceLog.Error($"LOAD failed slot {slot}: disk read error (unexpected): {ex}");
                return false;
            }

            if (bytes == null)
            {
                PersistenceLog.Warn($"No save in slot {slot}.");
                return false;
            }

            // Try main
            if (TryDeserializeAndApply(bytes, slot, isBackup: false))
                return true;

            byte[] bak = null;
            try
            {
                bak = _backend.ReadBackup(slot);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is NotSupportedException)
            {
                PersistenceLog.Error($"LOAD failed slot {slot}: backup read error: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                PersistenceLog.Error($"LOAD failed slot {slot}: backup read error (unexpected): {ex}");
                return false;
            }

            if (bak == null)
                return false;

            // Try backup deserialize/apply
            return TryDeserializeAndApply(bak, slot, isBackup: true);
        }

        private bool TryDeserializeAndApply(byte[] data, int slot, bool isBackup)
        {
            try
            {
                var pkg = SaveSerializer.Deserialize(data);
                LastLoadedPackage = pkg;

                ApplyPackageToRAM(pkg);

                var sceneId = pkg.ActiveSceneId ?? "";
                var sceneLoad = pkg.ActiveSceneLoad ?? "";

                if (!isBackup)
                {
                    PersistenceLog.Info($"Loaded slot {slot} (v{pkg.Version}, kind={pkg.Kind}, sceneId='{sceneId}', load='{sceneLoad}')");
                }
                else
                {
                    PersistenceLog.Warn($"Loaded BACKUP for slot {slot} (v{pkg.Version}, kind={pkg.Kind}, sceneId='{sceneId}', load='{sceneLoad}')");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (!isBackup)
                    PersistenceLog.Error($"Failed to load slot {slot}: {ex.GetType().Name}: {ex.Message}");
                else
                    PersistenceLog.Error($"Backup also failed slot {slot}: {ex.GetType().Name}: {ex.Message}");

                return false;
            }
        }

        /// Remap a loaded scene scope key in RAM (e.g., "name:Level01" -> "guid:abcd...") so ApplyScope hits.
        /// Intended to be called AFTER travel, once the runtime scope id is known.
        public bool TryRemapScopeKeyInRAM(string fromScopeKey, string toScopeKey)
        {
            fromScopeKey = SceneIdentity.NormalizeLegacy(fromScopeKey ?? "");
            toScopeKey = SceneIdentity.NormalizeLegacy(toScopeKey ?? "");

            if (SceneIdentity.IsEmptyId(fromScopeKey) || SceneIdentity.IsEmptyId(toScopeKey))
                return false;

            if (string.Equals(fromScopeKey, toScopeKey, StringComparison.Ordinal))
                return false;

            var world = PersistenceServices.Get<WorldStateService>();
            return world.State.TryRenameScope(fromScopeKey, toScopeKey, overwriteDestination: true);
        }

        private static SaveKind ParseKindOrUnknown(string kindName)
        {
            if (string.IsNullOrWhiteSpace(kindName))
                return SaveKind.Unknown;

            return Enum.TryParse(kindName, ignoreCase: true, out SaveKind k) ? k : SaveKind.Unknown;
        }

        private static SavePackage BuildPackageFromRAM(string activeSceneId, string activeSceneLoad)
        {
            var world = PersistenceServices.Get<WorldStateService>();
            var ram = world.State;

            if (string.IsNullOrWhiteSpace(activeSceneLoad))
                activeSceneLoad = SceneManager.GetActiveScene().name;

            if (string.IsNullOrWhiteSpace(activeSceneId))
                activeSceneId = "name:" + SceneManager.GetActiveScene().name;

            var pkg = new SavePackage
            {
                ActiveSceneId = activeSceneId,
                ActiveSceneLoad = activeSceneLoad,
                SavedUtcTicks = DateTime.UtcNow.Ticks,
                GlobalStateBlob = ram.GlobalStateBlob
            };

            foreach (var kv in ram.Scopes)
            {
                var scopeKey = kv.Key;
                var scope = kv.Value;

                var sr = new SavePackage.ScopeRecord { ScopeKey = scopeKey };

                foreach (var dead in scope.Destroyed)
                    sr.Destroyed.Add(dead);

                foreach (var eb in scope.EntityBlobs)
                {
                    if (!scope.DiskEligible.Contains(eb.Key))
                        continue;

                    sr.Entities.Add(new SavePackage.EntityRecord { EntityId = eb.Key, Blob = eb.Value });
                }

                pkg.Scopes.Add(sr);
            }

            return pkg;
        }

        private static void ApplyPackageToRAM(SavePackage pkg)
        {
            var world = PersistenceServices.Get<WorldStateService>();
            world.State.ClearAll();
            world.State.GlobalStateBlob = pkg.GlobalStateBlob;

            foreach (var s in pkg.Scopes)
            {
                var scope = world.State.GetOrCreate(s.ScopeKey);

                scope.Destroyed.Clear();
                scope.EntityBlobs.Clear();
                scope.DiskEligible.Clear();

                foreach (var dead in s.Destroyed)
                    scope.Destroyed.Add(dead);

                foreach (var e in s.Entities)
                {
                    scope.EntityBlobs[e.EntityId] = e.Blob;
                    scope.DiskEligible.Add(e.EntityId);
                }

                scope.BumpRevision();
            }
        }
    }
}
