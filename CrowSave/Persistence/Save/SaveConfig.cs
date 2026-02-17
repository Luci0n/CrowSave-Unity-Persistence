using UnityEngine;
using CrowSave.Persistence.Save.Loading;

namespace CrowSave.Persistence.Save
{
    public enum CaptureMode
    {
        [Tooltip(
            "Captures only entities marked dirty (IDirtyPersistent.IsDirty == true).\n" +
            "Recommended for performance. Gameplay code must call MarkDirty() when persistent state changes."
        )]
        DirtyOnly = 0,

        [Tooltip(
            "Captures every eligible entity in the active scope on each capture.\n" +
            "Useful for prototyping/debugging; more expensive on large scenes."
        )]
        All = 1
    }

    public enum LoadScenePolicy
    {
        [Tooltip(
            "Applies loaded state to the currently active scene scope.\n" +
            "Use this when Load should not move the player to the saved scene."
        )]
        StayInCurrentScene = 0,

        [Tooltip(
            "On load, travel to the scene stored in the save file and apply there.\n" +
            "Recommended for standard \"Load Game\" behavior."
        )]
        TravelToSavedScene = 1
    }

    [CreateAssetMenu(menuName = "CrowSave/Save Config", fileName = "SaveConfig")]
    public sealed class SaveConfig : ScriptableObject
    {
        [Header("Scene Identity")]

        [Tooltip(
            "Controls how CrowSave identifies a scene for persistence scopes.\n" +
            "- SceneName: simple, but breaks on rename and can collide if two scenes share a filename.\n" +
            "- ScenePath: unique, but breaks on move/rename.\n" +
            "- SceneGuid: stable across rename/move, requires a SceneGuid component in each scene."
        )]
        public SceneIdentityMode sceneIdentityMode = SceneIdentityMode.SceneName;

        [Tooltip(
            "Editor-only: when SceneGuid mode is selected, warn if scenes in Build Settings are missing a SceneGuid component."
        )]
        public bool warnIfSceneGuidMissing = true;

        [Header("Scene GUID (Runtime)")]
        [Tooltip(
            "Required for SceneGuid mode travel at runtime.\n" +
            "The registry maps SceneGuid.guid -> buildIndex/path.\n" +
            "If null, GUID-based travel will fall back to name/path (best effort)."
        )]
        public SceneGuidRegistry sceneGuidRegistry;

        [Header("Capture")]

        [Tooltip(
            "Controls how scene/entity state is captured into RAM.\n" +
            "- DirtyOnly: capture only dirty entities.\n" +
            "- All: capture all eligible entities."
        )]
        public CaptureMode captureMode = CaptureMode.DirtyOnly;

        [Header("Checkpoints")]

        [Tooltip(
            "Number of rotating checkpoint slots used by the checkpoint ring.\n" +
            "Example: ring size 5 with base slot 50 produces slots 50..54.\n" +
            "Set to 0 to disable checkpoints."
        )]
        [Min(0)] public int checkpointRingSize = 5;

        [Tooltip(
            "If enabled, a checkpoint is written automatically during scene transitions.\n" +
            "A checkpoint uses LoadingOperation.Checkpoint and writes to the checkpoint ring slot."
        )]
        public bool checkpointOnSceneTransition = true;

        [Header("Autosave")]

        [Tooltip(
            "If enabled, an autosave is written automatically during scene transitions.\n" +
            "Autosave uses the normal save pipeline but typically targets a fixed slot (Autosave Slot)."
        )]
        public bool autosaveOnTransition = false;

        [Tooltip(
            "Disk slot used for autosaves when Autosave On Transition is enabled.\n" +
            "Common convention is slot 0."
        )]
        [Min(0)] public int autosaveSlot = 0;

        [Header("Load")]

        [Tooltip(
            "Controls whether load applies in the current scene or first travels to the saved scene.\n" +
            "- StayInCurrentScene: apply to current scope.\n" +
            "- TravelToSavedScene: load the saved scene first, then apply."
        )]
        public LoadScenePolicy loadScenePolicy = LoadScenePolicy.TravelToSavedScene;

        [Tooltip(
            "Only relevant when LoadScenePolicy = StayInCurrentScene.\n" +
            "If enabled, reloads the current scene before applying the loaded RAM state.\n" +
            "Purpose: reset non-persistent objects back to scene defaults before persistence is applied."
        )]
        public bool reloadCurrentSceneOnLoadWhenStaying = true;

        [Header("Loading UI")]

        [Tooltip(
            "Optional loading screen prefab.\n" +
            "If assigned, it should provide an ILoadingScreen implementation (directly or via a controller).\n" +
            "If null, operations run without UI."
        )]
        public GameObject loadingScreenPrefab;

        [Tooltip(
            "Loading screen presentation for manual saves (LoadingOperation.Save).\n" +
            "None disables UI for saves; other modes depend on the ILoadingScreen implementation."
        )]
        public LoadingViewMode loadingViewOnSave = LoadingViewMode.None;

        [Tooltip("Loading screen presentation for loads (LoadingOperation.Load).")]
        public LoadingViewMode loadingViewOnLoad = LoadingViewMode.ProgressBar;

        [Tooltip("Loading screen presentation for scene transitions (LoadingOperation.Transition).")]
        public LoadingViewMode loadingViewOnTransition = LoadingViewMode.ProgressBar;

        [Tooltip(
            "Determines how progress is computed/presented to the loading screen.\n" +
            "- PipelineWeighted: uses orchestrator pipeline weighted progress.\n" +
            "- SceneAsyncOnly: emphasizes Unity AsyncOperation.progress when scene loading occurs.\n" +
            "- Indeterminate: always show as indeterminate (spinner/steps-only UI)."
        )]
        public LoadingProgressSource loadingProgressSource = LoadingProgressSource.PipelineWeighted;

        [Header("Loading Timing")]

        [Tooltip(
            "Minimum time (seconds) the loading screen remains visible once shown.\n" +
            "Useful to prevent flicker on very fast operations."
        )]
        [Min(0f)] public float minLoadingScreenTime = 0.25f;

        [Tooltip(
            "Extra hold time (seconds) after progress reaches 100% before hiding the loading screen.\n" +
            "Used for polish and to avoid abrupt UI cuts."
        )]
        [Min(0f)] public float holdAtFullTime = 0.10f;

        [Header("Safety")]

        [Tooltip(
            "If enabled, Time.timeScale is set to 0 while save/load/transition operations run.\n" +
            "Prevents gameplay updates during critical persistence operations."
        )]
        public bool freezeTimeScaleDuringOps = true;

        [Header("Debug")]

        [Tooltip(
            "Enables detailed persistence logs.\n" +
            "Disable in production if log volume is excessive."
        )]
        public bool verboseLogs = true;
    }
}
