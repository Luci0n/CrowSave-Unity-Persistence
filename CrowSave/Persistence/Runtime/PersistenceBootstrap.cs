using System;
using System.Collections.Generic;
using CrowSave.Persistence.Save;
using CrowSave.Persistence.Save.Loading;
using UnityEngine;

namespace CrowSave.Persistence.Runtime
{
    [DefaultExecutionOrder(-10000)]
    public sealed class PersistenceBootstrap : MonoBehaviour
    {
        [Header("Disk")]
        [SerializeField] private string saveFolderName = "saves";

        [Header("Orchestrator")]
        [SerializeField] private SaveOrchestrator orchestrator;

        private ServiceContainer _container;

        // Guards against multiple bootstraps initializing in the same frame/order.
        private static bool s_bootstrapping;
        private static bool s_bootstrapped;

        private void Awake()
        {
            if (PersistenceServices.IsReady || s_bootstrapped || s_bootstrapping)
            {
                Destroy(gameObject);
                return;
            }

            // If a different DdolRoot already exists anywhere (including DontDestroyOnLoad),
            // we should NOT initialize a second container for even one frame.
            if (TryFindExistingRootOtherThanThis(out var otherRoot))
            {
                PersistenceLog.Warn(
                    $"PersistenceBootstrap skipped: existing DdolRoot already present on '{otherRoot.gameObject.name}'. Destroying duplicate bootstrap '{gameObject.name}'.",
                    this
                );

                Destroy(gameObject);
                return;
            }

            s_bootstrapping = true;

            try
            {
                // Ensure this object becomes the root (only when we're truly the first).
                if (GetComponent<DdolRoot>() == null)
                    gameObject.AddComponent<DdolRoot>();

                _container = new ServiceContainer();

                var registry = new PersistenceRegistry();
                var worldState = new WorldStateService();
                var captureApply = new CaptureApplyService(registry, worldState);

                var backend = new DiskSaveBackend(saveFolderName);
                var saveManager = new SaveManager(backend);

                _container.Register(registry);
                _container.Register(worldState);
                _container.Register(captureApply);
                _container.Register(saveManager);

                if (orchestrator == null)
                    orchestrator = GetComponent<SaveOrchestrator>();
                if (orchestrator == null)
                    orchestrator = gameObject.AddComponent<SaveOrchestrator>();

                _container.Register(orchestrator);

                // Optional Loading UI Controller (prefab comes from SaveConfig on orchestrator)
                var cfg = orchestrator.GetConfig();
                var loading = new LoadingUIController(cfg != null ? cfg.loadingScreenPrefab : null);
                _container.Register(loading);

                PersistenceServices.Bind(_container);

                s_bootstrapped = true;
                PersistenceLog.Info("Bootstrap initialized & services bound (including SaveOrchestrator + LoadingUIController).", this);
            }
            catch (Exception ex)
            {
                // If init fails, don’t leave the guard stuck.
                PersistenceLog.Error($"PersistenceBootstrap init failed: {ex}", this);
                s_bootstrapped = false;
                Destroy(gameObject);
            }
            finally
            {
                s_bootstrapping = false;
            }
        }

        /// <summary>
        /// Finds an existing DdolRoot instance that is not on this GameObject.
        /// Uses Resources.FindObjectsOfTypeAll to include DontDestroyOnLoad + inactive objects.
        /// Filters out prefab assets (scene invalid).
        /// </summary>
        private bool TryFindExistingRootOtherThanThis(out DdolRoot otherRoot)
        {
            otherRoot = null;

            // Includes inactive + DontDestroyOnLoad scene objects.
            var all = Resources.FindObjectsOfTypeAll<DdolRoot>();
            if (all == null || all.Length == 0) return false;

            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null) continue;
                var go = r.gameObject;
                if (go == null) continue;
                if (!go.scene.IsValid()) continue;
                if (go == gameObject) continue;
                otherRoot = r;
                return true;
            }

            return false;
        }
    }
}
