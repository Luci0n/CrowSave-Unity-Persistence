using CrowSave.Persistence.Runtime;
using UnityEngine;

namespace CrowSave.Persistence.Save.Loading
{
    /// <summary>
    /// Owns a loading screen prefab (optional). Spawns it on demand and forwards updates.
    /// Null-safe: if no prefab assigned, does nothing.
    /// </summary>
    public sealed class LoadingUIController
    {
        private readonly GameObject _prefab;
        private GameObject _instance;
        private ILoadingScreen _screen;

        public bool IsActive => _instance != null;

        public LoadingUIController(GameObject loadingScreenPrefab)
        {
            _prefab = loadingScreenPrefab;
        }

        public void Begin(LoadingContext ctx, LoadingViewMode viewMode)
        {
            if (_prefab == null || viewMode == LoadingViewMode.None) return;

            if (_instance == null)
            {
                _instance = Object.Instantiate(_prefab);
                Object.DontDestroyOnLoad(_instance);

                _screen = _instance.GetComponentInChildren<ILoadingScreen>();
                if (_screen == null)
                {
                    PersistenceLog.Warn("Loading screen prefab has no component implementing ILoadingScreen. UI disabled.");
                    Object.Destroy(_instance);
                    _instance = null;
                    return;
                }
            }

            _screen.Show(ctx, viewMode);
        }

        public void Update(string status, float progress01)
        {
            if (_screen == null) return;
            _screen.SetStatus(status);
            _screen.SetProgress(progress01);
        }

        public void UpdateSteps(string[] steps, int activeIndex)
        {
            if (_screen == null) return;
            _screen.SetSteps(steps, activeIndex);
        }

        public void End()
        {
            if (_screen == null) return;
            _screen.Hide();
        }
    }
}
