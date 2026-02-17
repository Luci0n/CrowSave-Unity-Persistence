using System;
using System.Collections.Generic;
using System.Linq;
using CrowSave.Persistence.Core;
using CrowSave.Persistence.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrowSave.Persistence.Save.Pipeline
{
    /// A single scene-load reference that can represent name, path, build index, or asset GUID.
    /// - Name: loads by scene name (must be in Build Settings)
    /// - Path: resolves to build index via SceneUtility.GetBuildIndexByScenePath(path)
    /// - BuildIndex: loads by build index
    /// - Guid: resolves via SaveOrchestrator.Instance.GetConfig().sceneGuidRegistry (GUID -> buildIndex/path)
    public readonly struct SceneLoadRef
    {
        public enum Kind { Name = 0, Path = 1, BuildIndex = 2, Guid = 3 }

        public readonly Kind Type;

        public readonly string Name;
        public readonly string Path;
        public readonly int BuildIndex;
        public readonly string Guid;

        private SceneLoadRef(Kind type, string name, string path, int buildIndex, string guid)
        {
            Type = type;
            Name = name;
            Path = path;
            BuildIndex = buildIndex;
            Guid = guid;
        }

        public static SceneLoadRef FromName(string name)
            => new SceneLoadRef(Kind.Name, name ?? "", null, -1, null);

        public static SceneLoadRef FromPath(string path)
        {
            path ??= "";
            int idx = SceneUtility.GetBuildIndexByScenePath(path);
            return new SceneLoadRef(Kind.Path, null, path, idx, null);
        }

        public static SceneLoadRef FromBuildIndex(int buildIndex)
            => new SceneLoadRef(Kind.BuildIndex, null, null, buildIndex, null);

        public static SceneLoadRef FromGuid(string guid)
            => new SceneLoadRef(Kind.Guid, null, null, -1, guid ?? "");

        public override string ToString()
        {
            return Type switch
            {
                Kind.BuildIndex => $"buildIndex:{BuildIndex}",
                Kind.Path => $"path:{Path}",
                Kind.Guid => $"guid:{Guid}",
                _ => $"name:{Name}"
            };
        }
    }

    /// <summary>Capture current scope into RAM (dirty or all). Intent controls disk eligibility side effects.</summary>
    public sealed class TaskCaptureScope : ILoadTask
    {
        private readonly string _scopeKey;
        private readonly CaptureMode _mode;
        private readonly CaptureIntent _intent;

        private bool _done;
        private float _progress;

        public string Name => _mode == CaptureMode.All ? $"Capture ALL ({_scopeKey})" : $"Capture DIRTY ({_scopeKey})";
        public float Progress => _progress;
        public bool IsDone => _done;

        public TaskCaptureScope(string scopeKey, CaptureMode mode, CaptureIntent intent)
        {
            _scopeKey = scopeKey;
            _mode = mode;
            _intent = intent;
        }

        public void Begin()
        {
            _done = false;
            _progress = 0f;
        }

        public void Tick()
        {
            if (_done) return;

            var cap = PersistenceServices.Get<CaptureApplyService>();
            _progress = 0.25f;

            if (_mode == CaptureMode.All) cap.CaptureAll(_scopeKey, _intent);
            else cap.CaptureDirty(_scopeKey, _intent);

            _progress = 1f;
            _done = true;
        }
    }

    /// <summary>Write RAM -> disk slot.</summary>
    public sealed class TaskSaveDisk : ILoadTask
    {
        private readonly int _slot;
        private bool _done;

        public string Name => $"Write Save (slot {_slot})";
        public float Progress => _done ? 1f : 0f;
        public bool IsDone => _done;

        public TaskSaveDisk(int slot) => _slot = slot;

        public void Begin() { _done = false; }

        public void Tick()
        {
            if (_done) return;
            var sm = PersistenceServices.Get<SaveManager>();
            sm.SaveSlotFromRAM(_slot);
            _done = true;
        }
    }

    /// <summary>Read disk slot -> RAM. Exposes loaded metadata.</summary>
    public sealed class TaskLoadDisk : ILoadTask
    {
        private readonly int _slot;
        private bool _done;
        private float _progress;

        public bool Success { get; private set; }

        // v4+
        public string LoadedActiveSceneId { get; private set; }
        public string LoadedActiveSceneLoad { get; private set; }

        public string Name => $"Read Save (slot {_slot})";
        public float Progress => _progress;
        public bool IsDone => _done;

        public TaskLoadDisk(int slot) => _slot = slot;

        public void Begin()
        {
            _done = false;
            _progress = 0f;
            Success = false;
            LoadedActiveSceneId = null;
            LoadedActiveSceneLoad = null;
        }

        public void Tick()
        {
            if (_done) return;

            var sm = PersistenceServices.Get<SaveManager>();
            _progress = 0.25f;

            Success = sm.LoadSlotToRAM(_slot);
            _progress = 1f;

            var pkg = sm.LastLoadedPackage;
            LoadedActiveSceneId = pkg?.ActiveSceneId;
            LoadedActiveSceneLoad = pkg?.ActiveSceneLoad;

            _done = true;
        }
    }

    /// <summary>
    /// Async scene load with progress.
    /// Supports Name/Path/BuildIndex/Guid (Guid requires registry in SaveConfig).
    /// Also supports parsing typed strings like "name:Level01", "path:Assets/..", "guid:...".
    /// Legacy strings (no prefix) are treated as Name.
    /// - Exposes Success.
    /// - If ref can't be resolved or async load can't be started, completes Success=false.
    /// - If it reaches isDone, Success=true.
    /// </summary>
    public sealed class TaskLoadSceneAsync : ILoadTask
    {
        private readonly SceneLoadRef _scene;
        private readonly SceneGuidRegistry _guidRegistry;
        private AsyncOperation _op;
        private bool _done;

        public bool Success { get; private set; }

        public string Name => _scene.Type switch
        {
            SceneLoadRef.Kind.BuildIndex => $"Load Scene (index {_scene.BuildIndex})",
            SceneLoadRef.Kind.Path => $"Load Scene (path)",
            SceneLoadRef.Kind.Guid => $"Load Scene (guid)",
            _ => $"Load Scene '{_scene.Name}'"
        };

        public float Progress
        {
            get
            {
                if (_done && Success) return 1f;
                if (_op == null) return 0f;
                if (_op.isDone) return 1f;
                return Mathf.Clamp01(_op.progress / 0.9f);
            }
        }

        public bool IsDone => _done;

        public TaskLoadSceneAsync(string sceneNameOrTypedKey, SceneGuidRegistry guidRegistry = null) : this(Parse(sceneNameOrTypedKey), guidRegistry) { }
        public TaskLoadSceneAsync(SceneLoadRef scene, SceneGuidRegistry guidRegistry = null)
        {
            _scene = scene;
            _guidRegistry = guidRegistry;
        }

        public void Begin()
        {
            _done = false;
            Success = false;
            _op = null;

            try
            {
                if (!TryResolveToLoad(out var loadKind, out var loadName, out var loadBuildIndex))
                {
                    _done = true;
                    return;
                }

                if (loadKind == ResolvedLoadKind.BuildIndex)
                {
                    if (loadBuildIndex < 0)
                    {
                        PersistenceLog.Error($"TaskLoadSceneAsync: Invalid build index ({loadBuildIndex}). Scene load aborted.");
                        _done = true;
                        return;
                    }

                    _op = SceneManager.LoadSceneAsync(loadBuildIndex, LoadSceneMode.Single);
                    if (_op == null)
                    {
                        PersistenceLog.Error($"TaskLoadSceneAsync: LoadSceneAsync returned null for build index {loadBuildIndex}.");
                        _done = true;
                        return;
                    }

                    _op.allowSceneActivation = true;
                    return;
                }

                if (string.IsNullOrWhiteSpace(loadName))
                {
                    PersistenceLog.Error("TaskLoadSceneAsync: Empty scene name. Scene load aborted.");
                    _done = true;
                    return;
                }

                if (!Application.CanStreamedLevelBeLoaded(loadName))
                {
                    PersistenceLog.Error($"TaskLoadSceneAsync: Scene '{loadName}' cannot be loaded (not in Build Settings?).");
                    _done = true;
                    return;
                }

                _op = SceneManager.LoadSceneAsync(loadName, LoadSceneMode.Single);
                if (_op == null)
                {
                    PersistenceLog.Error($"TaskLoadSceneAsync: LoadSceneAsync returned null for scene name '{loadName}'.");
                    _done = true;
                    return;
                }

                _op.allowSceneActivation = true;
            }
            catch (Exception ex)
            {
                PersistenceLog.Error($"TaskLoadSceneAsync: exception while starting load: {ex}");
                _done = true;
                Success = false;
            }
        }

        public void Tick()
        {
            if (_done) return;

            if (_op == null)
            {
                _done = true;
                Success = false;
                return;
            }

            if (_op.isDone)
            {
                Success = true;
                _done = true;
            }
        }

        private enum ResolvedLoadKind { Name, BuildIndex }

        private bool TryResolveToLoad(out ResolvedLoadKind kind, out string sceneName, out int buildIndex)
        {
            kind = ResolvedLoadKind.Name;
            sceneName = null;
            buildIndex = -1;

            switch (_scene.Type)
            {
                case SceneLoadRef.Kind.BuildIndex:
                    kind = ResolvedLoadKind.BuildIndex;
                    buildIndex = _scene.BuildIndex;
                    return true;

                case SceneLoadRef.Kind.Path:
                {
                    int idx = SceneUtility.GetBuildIndexByScenePath(_scene.Path ?? "");
                    if (idx >= 0)
                    {
                        kind = ResolvedLoadKind.BuildIndex;
                        buildIndex = idx;
                        return true;
                    }

                    var fallbackName = ExtractNameFromPath(_scene.Path);
                    if (!string.IsNullOrWhiteSpace(fallbackName))
                    {
                        kind = ResolvedLoadKind.Name;
                        sceneName = fallbackName;
                        return true;
                    }

                    PersistenceLog.Error($"TaskLoadSceneAsync: Scene path not in Build Settings: '{_scene.Path}'");
                    return false;
                }

                case SceneLoadRef.Kind.Guid:
                {
                    var reg = _guidRegistry;

                    if (reg != null && reg.TryResolve(_scene.Guid, out var entry))
                    {
                        if (entry.buildIndex >= 0)
                        {
                            kind = ResolvedLoadKind.BuildIndex;
                            buildIndex = entry.buildIndex;
                            return true;
                        }

                        if (!string.IsNullOrWhiteSpace(entry.scenePath))
                        {
                            int idx = SceneUtility.GetBuildIndexByScenePath(entry.scenePath);
                            if (idx >= 0)
                            {
                                kind = ResolvedLoadKind.BuildIndex;
                                buildIndex = idx;
                                return true;
                            }

                            var fallbackName = ExtractNameFromPath(entry.scenePath);
                            if (!string.IsNullOrWhiteSpace(fallbackName))
                            {
                                kind = ResolvedLoadKind.Name;
                                sceneName = fallbackName;
                                return true;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(entry.sceneName))
                        {
                            kind = ResolvedLoadKind.Name;
                            sceneName = entry.sceneName;
                            return true;
                        }

                        PersistenceLog.Error($"TaskLoadSceneAsync: Registry entry for guid '{_scene.Guid}' has no usable buildIndex/path/name.");
                        return false;
                    }

                    PersistenceLog.Error($"TaskLoadSceneAsync: No registry entry for guid '{_scene.Guid}'. Assign SaveConfig.sceneGuidRegistry.");
                    return false;
                }

                case SceneLoadRef.Kind.Name:
                default:
                    kind = ResolvedLoadKind.Name;
                    sceneName = _scene.Name;
                    return true;
            }
        }

        private static string ExtractNameFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            int slash = path.LastIndexOf('/');
            string file = slash >= 0 ? path.Substring(slash + 1) : path;
            if (file.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                file = file.Substring(0, file.Length - 6);
            return file;
        }

        public static SceneLoadRef Parse(string value)
        {
            value ??= "";

            if (!value.Contains(":"))
                return SceneLoadRef.FromName(value);

            if (value.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                return SceneLoadRef.FromName(value.Substring("name:".Length));

            if (value.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
                return SceneLoadRef.FromPath(value.Substring("path:".Length));

            if (value.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
                return SceneLoadRef.FromGuid(value.Substring("guid:".Length));

            return SceneLoadRef.FromName(value);
        }
    }

    /// <summary>
    /// Wait until the registry for the given scope "stabilizes" for N frames.
    /// Uses scope REVISION stability rather than Count stability.
    /// </summary>
    public sealed class TaskWaitForScopeReady : ILoadTask
    {
        private readonly string _scopeKey;
        private readonly int _stableFramesRequired;
        private readonly int _minFrames;
        private readonly int _maxFrames;

        private bool _done;
        private bool _timedOutWarned;

        private int _frames;
        private int _stableFrames;

        private int _lastRevision;

        private bool _requireNonZeroCount;

        public string Name => $"Wait Scope Ready ({_scopeKey})";

        public float Progress
        {
            get
            {
                if (_done) return 1f;
                if (_stableFramesRequired <= 0) return 1f;
                return Mathf.Clamp01((float)_stableFrames / _stableFramesRequired);
            }
        }

        public bool IsDone => _done;

        public TaskWaitForScopeReady(string scopeKey, int stableFramesRequired = 2, int minFrames = 1, int maxFrames = 600)
        {
            _scopeKey = scopeKey;
            _stableFramesRequired = Mathf.Max(1, stableFramesRequired);
            _minFrames = Mathf.Max(0, minFrames);
            _maxFrames = Mathf.Max(_minFrames + 1, maxFrames);
        }

        public void Begin()
        {
            _done = false;
            _timedOutWarned = false;

            _frames = 0;
            _stableFrames = 0;

            _lastRevision = -1;

            _requireNonZeroCount = ScopeHasStateToApply(_scopeKey);
        }

        public void Tick()
        {
            if (_done) return;

            _frames++;

            var reg = PersistenceServices.Get<PersistenceRegistry>();
            int rev = reg.GetScopeRevision(_scopeKey);
            int count = reg.GetScopeCount(_scopeKey);

            bool revStable = (rev == _lastRevision);
            bool acceptableCount = !_requireNonZeroCount || count > 0;

            if (revStable && acceptableCount) _stableFrames++;
            else _stableFrames = 0;

            _lastRevision = rev;

            if (_frames >= _minFrames && _stableFrames >= _stableFramesRequired)
            {
                _done = true;
                return;
            }

            if (_frames >= _maxFrames)
            {
                if (_requireNonZeroCount && count == 0 && !_timedOutWarned)
                {
                    _timedOutWarned = true;
                    PersistenceLog.Warn(
                        $"TaskWaitForScopeReady TIMEOUT: scope '{_scopeKey}' still has 0 registered entities " +
                        $"but RAM contains state to apply. Proceeding anyway (apply may miss late registrants)."
                    );
                }

                _done = true;
            }
        }

        private static bool ScopeHasStateToApply(string scopeKey)
        {
            if (string.IsNullOrWhiteSpace(scopeKey)) return false;
            if (!PersistenceServices.IsReady) return false;

            if (!PersistenceServices.TryGet(out WorldStateService world)) return false;
            if (!world.State.TryGet(scopeKey, out var scope)) return false;

            return (scope.EntityBlobs.Count > 0) || (scope.Destroyed.Count > 0);
        }
    }

    /// <summary>Apply RAM state to the given scope.</summary>
    public sealed class TaskApplyScope : ILoadTask
    {
        private readonly string _scopeKey;
        private readonly ApplyReason _reason;
        private bool _done;
        private float _progress;

        public string Name => $"Apply State ({_scopeKey})";
        public float Progress => _progress;
        public bool IsDone => _done;

        public TaskApplyScope(string scopeKey, ApplyReason reason)
        {
            _scopeKey = scopeKey;
            _reason = reason;
        }

        public void Begin() { _done = false; _progress = 0f; }

        public void Tick()
        {
            if (_done) return;

            var cap = PersistenceServices.Get<CaptureApplyService>();
            _progress = 0.25f;
            cap.ApplyScope(_scopeKey, _reason);
            _progress = 1f;
            _done = true;
        }
    }

    public sealed class TaskReloadActiveSceneAsync : ILoadTask
    {
        private AsyncOperation _op;
        private bool _done;

        public string Name => "Reload Current Scene";
        public float Progress
        {
            get
            {
                if (_done) return 1f;
                if (_op == null) return 0f;
                if (_op.isDone) return 1f;
                return Mathf.Clamp01(_op.progress / 0.9f);
            }
        }
        public bool IsDone => _done;

        public void Begin()
        {
            _done = false;
            _op = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
            if (_op != null) _op.allowSceneActivation = true;
        }

        public void Tick()
        {
            if (_done) return;
            if (_op == null) { _done = true; return; }
            if (_op.isDone) _done = true;
        }
    }

    /// <summary>Just waits N frames (useful between steps).</summary>
    public sealed class TaskWaitFrames : ILoadTask
    {
        private readonly int _framesToWait;
        private int _frames;
        private bool _done;

        public string Name => $"Wait ({_framesToWait}f)";
        public float Progress => _framesToWait <= 0 ? 1f : Mathf.Clamp01((float)_frames / _framesToWait);
        public bool IsDone => _done;

        public TaskWaitFrames(int framesToWait) => _framesToWait = Mathf.Max(0, framesToWait);

        public void Begin()
        {
            _frames = 0;
            _done = _framesToWait == 0;
        }

        public void Tick()
        {
            if (_done) return;
            _frames++;
            if (_frames >= _framesToWait) _done = true;
        }
    }
}
