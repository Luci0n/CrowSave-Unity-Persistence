using System;
using System.Collections;
using CrowSave.Persistence.Core;
using CrowSave.Persistence.Runtime;
using CrowSave.Persistence.Save.Loading;
using CrowSave.Persistence.Save.Pipeline;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrowSave.Persistence.Save
{
// Features and Usage:
// - Singleton DDOL; keep exactly one instance.
// - One op at a time: if IsBusy==true, calls are refused (UI should disable/queue).
// - Calls start coroutines: Checkpoint(), SaveSlot(slot), LoadSlot(slot[,travel]), TransitionToScene(key).
// - Can freeze Time.timeScale during ops (cfg.freezeTimeScaleDuringOps); UI clamps use unscaled time.
// - Loading UI is optional: fetched from PersistenceServices (LoadingUIController). Headless is OK.
// - Progress: CurrentTaskName / CurrentProgress updated from pipeline each frame; cfg can force indeterminate display.
// - Save pipeline: Capture scene scope -> (optional) Capture global scope -> SaveDisk.
// - Load pipeline: LoadDisk -> (travel/reload/wait) -> Barrier(wait scope ready) -> Apply global+scene -> Seed RAM.
    public sealed class SaveOrchestrator : MonoBehaviour
    {
        public static SaveOrchestrator Instance { get; private set; }

        [SerializeField] private SaveConfig config;

        private bool _busy;
        private int _checkpointIndex = 0;

        public string CurrentTaskName { get; private set; } = "(idle)";
        public float CurrentProgress { get; private set; } = 0f;

        public bool IsBusy => _busy;
        public SaveConfig GetConfig() => config;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (config == null)
                PersistenceLog.Warn("SaveOrchestrator has no SaveConfig assigned. Using defaults.", this);
        }

        public void Checkpoint(string reason = null)
        {
            if (_busy) { PersistenceLog.Warn("Checkpoint requested but orchestrator is busy."); return; }
            StartCoroutine(CheckpointRoutine(reason));
        }

        public void SaveSlot(int slot)
        {
            if (_busy) { PersistenceLog.Warn("SaveSlot requested but orchestrator is busy."); return; }
            StartCoroutine(SaveSlotRoutine_Wrapper(slot, SaveKind.Manual, note: null, showUI: true));
        }

        private IEnumerator SaveSlotRoutine_Wrapper(int slot, SaveKind kind, string note, bool showUI)
        {
            _busy = true;
            try
            {
                yield return SaveSlotRoutine(slot, kind, note, showUI);
            }
            finally
            {
                _busy = false;
                CurrentTaskName = "(idle)";
                CurrentProgress = 0f;
            }
        }

        public void LoadSlot(int slot)
        {
            bool travel = config == null || config.loadScenePolicy == LoadScenePolicy.TravelToSavedScene;
            LoadSlot(slot, travel);
        }

        public void LoadSlot(int slot, bool travelToSavedScene)
        {
            if (_busy) { PersistenceLog.Warn("LoadSlot requested but orchestrator is busy."); return; }
            StartCoroutine(LoadSlotRoutine(slot, travelToSavedScene));
        }

        public void TransitionToScene(string sceneNameOrLoadKey)
        {
            if (_busy) { PersistenceLog.Warn("Transition requested but orchestrator is busy."); return; }
            StartCoroutine(TransitionRoutine(sceneNameOrLoadKey));
        }

        private IEnumerator CheckpointRoutine(string reason)
        {
            _busy = true;
            try
            {
                var cfg = config;
                if (cfg != null) PersistenceLog.Enabled = cfg.verboseLogs;

                using (new OpScope(this, cfg, $"Checkpoint {(string.IsNullOrWhiteSpace(reason) ? "" : $"({reason})")}"))
                {
                    if (cfg == null || cfg.checkpointRingSize <= 0)
                    {
                        PersistenceLog.Warn("Checkpoint ignored: checkpointRingSize is 0.");
                        yield break;
                    }

                    int slot = ComputeCheckpointSlot();
                    _checkpointIndex = (_checkpointIndex + 1) % cfg.checkpointRingSize;

                    yield return SaveSlotRoutine(slot, SaveKind.Checkpoint, note: reason, showUI: true);
                }
            }
            finally
            {
                _busy = false;
                CurrentTaskName = "(idle)";
                CurrentProgress = 0f;
            }
        }

        private static CaptureIntent GetSaveIntent(SaveKind kind)
        {
            switch (kind)
            {
                case SaveKind.Autosave:   return CaptureIntent.DiskAutosave;
                case SaveKind.Checkpoint: return CaptureIntent.DiskCheckpoint;
                default:                  return CaptureIntent.DiskManualSave;
            }
        }

        private static LoadingOperation GetSaveOperation(SaveKind kind)
        {
            switch (kind)
            {
                case SaveKind.Checkpoint: return LoadingOperation.Checkpoint;
                case SaveKind.Autosave:
                case SaveKind.Manual:
                default:                  return LoadingOperation.Save;
            }
        }

        private static string GetKindName(SaveKind kind)
            => kind == SaveKind.Unknown ? null : kind.ToString();

        private IEnumerator SaveSlotRoutine(int slot, SaveKind kind, string note, bool showUI)
        {
            var cfg = config;
            if (cfg != null) PersistenceLog.Enabled = cfg.verboseLogs;

            var loading = showUI ? TryGetLoadingUI() : null;
            var view = (showUI && cfg != null) ? cfg.loadingViewOnSave : LoadingViewMode.None;

            var activeScene = SceneManager.GetActiveScene();

            string sceneScopeId = SceneIdentity.GetSceneId(activeScene, cfg != null ? cfg.sceneIdentityMode : SceneIdentityMode.SceneName);
            string sceneLoadKey = SceneLoadResolver.BuildActiveSceneLoadKey(activeScene, cfg);

            var ctx = new LoadingContext(
                GetSaveOperation(kind),
                slot: slot,
                fromScene: activeScene.name
            );

            using (new OpScope(this, cfg, $"{(string.IsNullOrWhiteSpace(GetKindName(kind)) ? "Save" : GetKindName(kind))} slot={slot}{(showUI ? "" : " (no UI)") }"))
            {
                float started = -1f;
                if (loading != null && view != LoadingViewMode.None)
                {
                    loading.Begin(ctx, view);
                    started = Time.unscaledTime;
                }

                string globalScope = PersistentId.GlobalScopeKey;

                string kindName = GetKindName(kind);
                var intent = GetSaveIntent(kind);

                if (PersistenceServices.TryGet(out SaveManager sm))
                    sm.SetNextSaveHeader(sceneScopeId, sceneLoadKey, kindName, note);

                var pipeline = new LoadPipeline();

                pipeline.Add(new TaskCaptureScope(sceneScopeId, cfg != null ? cfg.captureMode : CaptureMode.DirtyOnly, intent), weight: 2f);

                if (HasAnyInScope(globalScope))
                    pipeline.Add(new TaskCaptureScope(globalScope, cfg != null ? cfg.captureMode : CaptureMode.DirtyOnly, intent), weight: 1f);

                pipeline.Add(new TaskSaveDisk(slot), weight: 2f);

                yield return RunPipeline(pipeline, loading, cfg);

                if (loading != null && view != LoadingViewMode.None)
                    yield return EndLoadingWithClamp(loading, cfg, started);

                if (!showUI)
                    PersistenceLog.Info($"SAVE DONE slot={slot} kind='{kindName ?? ""}' note='{note ?? ""}' (no UI)");
                else
                    PersistenceLog.Info($"SAVE DONE slot={slot}");
            }
        }

        private IEnumerator LoadSlotRoutine(int slot, bool travelToSavedScene)
        {
            _busy = true;
            try
            {
                var cfg = config;
                if (cfg != null) PersistenceLog.Enabled = cfg.verboseLogs;

                var loading = TryGetLoadingUI();
                var view = cfg != null ? cfg.loadingViewOnLoad : LoadingViewMode.None;

                var ctx = new LoadingContext(
                    LoadingOperation.Load,
                    slot: slot,
                    fromScene: SceneManager.GetActiveScene().name,
                    travel: travelToSavedScene
                );

                using (new OpScope(this, cfg, $"Load slot={slot} travel={travelToSavedScene}"))
                {
                    float started = -1f;
                    if (loading != null && view != LoadingViewMode.None)
                    {
                        loading.Begin(ctx, view);
                        started = Time.unscaledTime;
                    }

                    var loadDisk = new TaskLoadDisk(slot);
                    var p1 = new LoadPipeline();
                    p1.Add(loadDisk, weight: 2f);

                    loading?.UpdateSteps(new[] { "Read Save", "Travel/Wait", "Barrier", "Apply", "Seed RAM" }, 0);
                    yield return RunPipeline(p1, loading, cfg);

                    if (!loadDisk.Success)
                    {
                        if (loading != null && view != LoadingViewMode.None)
                            yield return EndLoadingWithClamp(loading, cfg, started);

                        PersistenceLog.Warn($"LOAD failed slot={slot}");
                        yield break;
                    }

                    string savedSceneId = SceneIdentity.NormalizeLegacy(loadDisk.LoadedActiveSceneId ?? "");
                    string savedSceneLoad = loadDisk.LoadedActiveSceneLoad ?? "";

                    var currentScene = SceneManager.GetActiveScene();
                    string currentSceneId = SceneIdentity.GetSceneId(
                        currentScene,
                        cfg != null ? cfg.sceneIdentityMode : SceneIdentityMode.SceneName
                    );

                    bool hasSavedScene = !SceneIdentity.IsEmptyId(savedSceneId) || !string.IsNullOrWhiteSpace(savedSceneLoad);
                    bool willTravel = travelToSavedScene && hasSavedScene;

                    if (willTravel && !SceneIdentity.IsEmptyId(savedSceneId))
                    {
                        if (string.Equals(currentSceneId, savedSceneId, StringComparison.Ordinal))
                            willTravel = false;
                    }

                    string globalScope = PersistentId.GlobalScopeKey;

                    var travelPipe = new LoadPipeline();
                    TaskLoadSceneAsync travelTask = null;

                    if (willTravel)
                    {
                        loading?.UpdateSteps(new[] { "Read Save", "Load Scene", "Barrier", "Apply", "Seed RAM" }, 1);

                        var loadRef = SceneLoadResolver.ResolveLoadRefPreferGuid(cfg, savedSceneId, savedSceneLoad);
                        travelTask = new TaskLoadSceneAsync(loadRef, cfg != null ? cfg.sceneGuidRegistry : null);
                        travelPipe.Add(travelTask, weight: 6f);
                    }
                    else
                    {
                        if (cfg != null && cfg.reloadCurrentSceneOnLoadWhenStaying)
                        {
                            loading?.UpdateSteps(new[] { "Read Save", "Reload Scene", "Barrier", "Apply", "Seed RAM" }, 1);
                            travelPipe.Add(new TaskReloadActiveSceneAsync(), weight: 6f);
                        }
                        else
                        {
                            loading?.UpdateSteps(new[] { "Read Save", "Wait", "Barrier", "Apply", "Seed RAM" }, 1);
                            travelPipe.Add(new TaskWaitFrames(1), weight: 1f);
                        }
                    }

                    yield return RunPipeline(travelPipe, loading, cfg);

                    if (willTravel && (travelTask == null || !travelTask.Success))
                    {
                        if (loading != null && view != LoadingViewMode.None)
                            yield return EndLoadingWithClamp(loading, cfg, started);

                        PersistenceLog.Error(
                            "LOAD aborted: travel was required but failed (scene could not be loaded). " +
                            "Not applying loaded RAM onto the current scene.",
                            this
                        );

                        yield break;
                    }

                    var sceneNow2 = SceneManager.GetActiveScene();
                    string applyScopeId = SceneIdentity.GetSceneId(
                        sceneNow2,
                        cfg != null ? cfg.sceneIdentityMode : SceneIdentityMode.SceneName
                    );

                    if (willTravel && !SceneIdentity.IsEmptyId(savedSceneId))
                    {
                        if (PersistenceServices.TryGet(out SaveManager sm))
                            sm.TryRemapScopeKeyInRAM(savedSceneId, applyScopeId);
                    }

                    var p2 = new LoadPipeline();

                    loading?.UpdateSteps(new[] { "Read Save", willTravel ? "Load Scene" : "Wait", "Barrier", "Apply", "Seed RAM" }, 2);
                    p2.Add(new TaskWaitForScopeReady(applyScopeId, stableFramesRequired: 2, minFrames: 1), weight: 2f);

                    loading?.UpdateSteps(new[] { "Read Save", willTravel ? "Load Scene" : "Wait", "Barrier", "Apply", "Seed RAM" }, 3);

                    if (HasAnyInScope(globalScope))
                        p2.Add(new TaskApplyScope(globalScope, ApplyReason.DiskLoad), weight: 2f);

                    p2.Add(new TaskApplyScope(applyScopeId, ApplyReason.DiskLoad), weight: 3f);

                    loading?.UpdateSteps(new[] { "Read Save", willTravel ? "Load Scene" : "Wait", "Barrier", "Apply", "Seed RAM" }, 4);

                    // IMPORTANT: "Seed RAM" capture should NOT touch disk eligibility.
                    if (HasAnyInScope(globalScope))
                        p2.Add(new TaskCaptureScope(globalScope, CaptureMode.All, CaptureIntent.Ram), weight: 1f);

                    p2.Add(new TaskCaptureScope(applyScopeId, CaptureMode.All, CaptureIntent.Ram), weight: 1f);

                    yield return RunPipeline(p2, loading, cfg);

                    if (loading != null && view != LoadingViewMode.None)
                        yield return EndLoadingWithClamp(loading, cfg, started);

                    PersistenceLog.Info(willTravel
                        ? $"LOAD+TRAVEL+APPLY DONE slot={slot} scope='{applyScopeId}' (+global)"
                        : $"LOAD+APPLY DONE slot={slot} scope='{applyScopeId}' (no travel, +global)");
                }
            }
            finally
            {
                _busy = false;
                CurrentTaskName = "(idle)";
                CurrentProgress = 0f;
            }
        }

        private IEnumerator TransitionRoutine(string sceneNameOrLoadKey)
        {
            _busy = true;
            try
            {
                var cfg = config;
                if (cfg != null) PersistenceLog.Enabled = cfg.verboseLogs;

                var loading = TryGetLoadingUI();
                var view = cfg != null ? cfg.loadingViewOnTransition : LoadingViewMode.None;

                var ctx = new LoadingContext(
                    LoadingOperation.Transition,
                    slot: -1,
                    fromScene: SceneManager.GetActiveScene().name,
                    toScene: sceneNameOrLoadKey
                );

                using (new OpScope(this, cfg, $"Transition -> {sceneNameOrLoadKey}"))
                {
                    float started = -1f;
                    if (loading != null && view != LoadingViewMode.None)
                    {
                        loading.Begin(ctx, view);
                        started = Time.unscaledTime;
                    }

                    var activeScene = SceneManager.GetActiveScene();
                    string currentScopeId = SceneIdentity.GetSceneId(
                        activeScene,
                        cfg != null ? cfg.sceneIdentityMode : SceneIdentityMode.SceneName
                    );
                    var globalScope = PersistentId.GlobalScopeKey;

                    if (cfg != null && cfg.checkpointOnSceneTransition && cfg.checkpointRingSize > 0)
                    {
                        int checkpointSlot = ComputeCheckpointSlot();
                        _checkpointIndex = (_checkpointIndex + 1) % cfg.checkpointRingSize;
                        yield return SaveSlotRoutine(checkpointSlot, SaveKind.Checkpoint, note: "transition", showUI: false);
                    }

                    if (cfg != null && cfg.autosaveOnTransition)
                    {
                        yield return SaveSlotRoutine(cfg.autosaveSlot, SaveKind.Autosave, note: "transition", showUI: false);
                    }

                    loading?.UpdateSteps(new[] { "Capture", "Load Scene", "Barrier", "Apply" }, 0);

                    var pipeline = new LoadPipeline();

                    // Transition capture is RAM-only
                    pipeline.Add(new TaskCaptureScope(currentScopeId, cfg != null ? cfg.captureMode : CaptureMode.DirtyOnly, CaptureIntent.Ram), weight: 2f);

                    if (HasAnyInScope(globalScope))
                        pipeline.Add(new TaskCaptureScope(globalScope, cfg != null ? cfg.captureMode : CaptureMode.DirtyOnly, CaptureIntent.Ram), weight: 1f);

                    loading?.UpdateSteps(new[] { "Capture", "Load Scene", "Barrier", "Apply" }, 1);
                    var destRef = SceneLoadResolver.ResolveLoadRef(cfg, sceneNameOrLoadKey);
                    pipeline.Add(new TaskLoadSceneAsync(destRef, cfg != null ? cfg.sceneGuidRegistry : null), weight: 6f);

                    yield return RunPipeline(pipeline, loading, cfg);

                    var afterLoadScene = SceneManager.GetActiveScene();
                    if (!SceneMatchesLoadRef(destRef, afterLoadScene))
                    {
                        if (loading != null && view != LoadingViewMode.None)
                            yield return EndLoadingWithClamp(loading, cfg, started);

                        PersistenceLog.Error(
                            $"TRANSITION load failed: expected '{destRef}' but active scene is '{afterLoadScene.name}' (buildIndex={afterLoadScene.buildIndex}). " +
                            $"Aborting apply to avoid applying destination RAM onto the wrong scene.",
                            this
                        );

                        yield break;
                    }

                    string destScopeId = SceneIdentity.GetSceneId(
                        afterLoadScene,
                        cfg != null ? cfg.sceneIdentityMode : SceneIdentityMode.SceneName
                    );

                    var p2 = new LoadPipeline();
                    loading?.UpdateSteps(new[] { "Capture", "Load Scene", "Barrier", "Apply" }, 2);
                    p2.Add(new TaskWaitForScopeReady(destScopeId, stableFramesRequired: 2, minFrames: 1), weight: 2f);

                    loading?.UpdateSteps(new[] { "Capture", "Load Scene", "Barrier", "Apply" }, 3);
                    if (HasAnyInScope(globalScope))
                        p2.Add(new TaskApplyScope(globalScope, ApplyReason.Transition), weight: 2f);

                    p2.Add(new TaskApplyScope(destScopeId, ApplyReason.Transition), weight: 3f);

                    yield return RunPipeline(p2, loading, cfg);

                    if (loading != null && view != LoadingViewMode.None)
                        yield return EndLoadingWithClamp(loading, cfg, started);

                    PersistenceLog.Info($"TRANSITION DONE -> '{SceneManager.GetActiveScene().name}' (+global)");
                }
            }
            finally
            {
                _busy = false;
                CurrentTaskName = "(idle)";
                CurrentProgress = 0f;
            }
        }

        private IEnumerator RunPipeline(LoadPipeline pipeline, LoadingUIController loading, SaveConfig cfg)
        {
            pipeline.Begin();

            while (!pipeline.IsDone)
            {
                pipeline.Tick();

                CurrentTaskName = pipeline.CurrentTaskName;
                CurrentProgress = pipeline.OverallProgress;

                float shownProgress = CurrentProgress;
                string shownStatus = CurrentTaskName;

                if (cfg != null)
                {
                    switch (cfg.loadingProgressSource)
                    {
                        case LoadingProgressSource.PipelineWeighted:
                        case LoadingProgressSource.SceneAsyncOnly:
                            shownProgress = CurrentProgress;
                            break;
                        case LoadingProgressSource.Indeterminate:
                            shownProgress = 0f;
                            break;
                    }
                }

                loading?.Update(shownStatus, shownProgress);

                yield return null;
            }

            CurrentTaskName = "(idle)";
            CurrentProgress = 0f;
        }

        private LoadingUIController TryGetLoadingUI()
        {
            if (!PersistenceServices.IsReady) return null;
            try { return PersistenceServices.Get<LoadingUIController>(); }
            catch { return null; }
        }

        private static IEnumerator EndLoadingWithClamp(LoadingUIController loading, SaveConfig cfg, float startedAtUnscaled)
        {
            if (loading == null) yield break;

            loading.Update("Finalizing...", 1f);

            float holdFull = cfg != null ? Mathf.Max(0f, cfg.holdAtFullTime) : 0f;
            if (holdFull > 0f)
            {
                float t = 0f;
                while (t < holdFull)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            float minTime = cfg != null ? Mathf.Max(0f, cfg.minLoadingScreenTime) : 0f;
            if (minTime > 0f && startedAtUnscaled >= 0f)
            {
                float elapsed = Time.unscaledTime - startedAtUnscaled;
                float remaining = minTime - elapsed;
                if (remaining > 0f)
                {
                    float t = 0f;
                    while (t < remaining)
                    {
                        t += Time.unscaledDeltaTime;
                        yield return null;
                    }
                }
            }

            loading.End();
        }

        private int ComputeCheckpointSlot()
        {
            const int baseSlot = 50;
            int size = Mathf.Max(1, config != null ? config.checkpointRingSize : 1);
            return baseSlot + (_checkpointIndex % size);
        }

        private static bool HasAnyInScope(string scopeKey)
        {
            if (!PersistenceServices.IsReady) return false;
            if (!PersistenceServices.TryGet(out PersistenceRegistry reg)) return false;
            return reg.GetAllInScope(scopeKey).Count > 0;
        }

        private static bool SceneMatchesLoadRef(SceneLoadRef expected, Scene activeScene)
        {
            switch (expected.Type)
            {
                case SceneLoadRef.Kind.BuildIndex:
                    return expected.BuildIndex >= 0 && activeScene.buildIndex == expected.BuildIndex;

                case SceneLoadRef.Kind.Name:
                    return !string.IsNullOrWhiteSpace(expected.Name) &&
                           string.Equals(activeScene.name, expected.Name, StringComparison.OrdinalIgnoreCase);

                case SceneLoadRef.Kind.Path:
                {
                    if (expected.BuildIndex >= 0)
                        return activeScene.buildIndex == expected.BuildIndex;

                    var fallbackName = ExtractNameFromPath(expected.Path);
                    return !string.IsNullOrWhiteSpace(fallbackName) &&
                           string.Equals(activeScene.name, fallbackName, StringComparison.OrdinalIgnoreCase);
                }

                case SceneLoadRef.Kind.Guid:
                    return false;

                default:
                    return false;
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

        private readonly struct OpScope : IDisposable
        {
            private readonly SaveOrchestrator _ctx;
            private readonly float _prevTimeScale;
            private readonly bool _freeze;

            public OpScope(SaveOrchestrator ctx, SaveConfig cfg, string name)
            {
                _ctx = ctx;
                _freeze = (cfg == null) ? true : cfg.freezeTimeScaleDuringOps;

                PersistenceLog.Info($"OP START: {name}", ctx);

                _prevTimeScale = Time.timeScale;
                if (_freeze) Time.timeScale = 0f;
            }

            public void Dispose()
            {
                if (_freeze) Time.timeScale = _prevTimeScale;
                PersistenceLog.Info("OP END", _ctx);
            }
        }
    }
}
