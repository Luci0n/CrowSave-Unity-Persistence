using CrowSave.Persistence.Core;
using CrowSave.Persistence.Runtime;
using UnityEngine;

namespace CrowSave.Flags.Runtime
{
    /// <summary>
    /// Persisted backing store for Flags (I/O state).
    /// Put this on a DDOL bootstrap object with a GLOBAL PersistentId.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class FlagsStatePersistence : PersistentMonoBehaviour, IApplyReasonedPersistent
    {
        public static FlagsStatePersistence Instance { get; private set; }

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private FlagsStore _store;
        private FlagsService _service;

        public FlagsService Service => _service;

        protected override void Awake()
        {
            base.Awake();

            if (Instance != null && Instance != this)
            {
                if (debugLogs)
                    Debug.LogWarning("[CrowSave.Flags] Duplicate FlagsStatePersistence -> destroying duplicate.", this);

                Destroy(gameObject);
                return;
            }

            Instance = this;

            _store ??= new FlagsStore();
            _service ??= new FlagsService(_store, markDirty: MarkDirty);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void Capture(IStateWriter w)
        {
            _store.Capture(w);

            if (debugLogs)
                Debug.Log($"[CrowSave.Flags][CAPTURE] rev={_service.Revision}", this);
        }

        public override void Apply(IStateReader r) => Apply(r, ApplyReason.Transition);

        public void Apply(IStateReader r, ApplyReason reason)
        {
            _store.Apply(r);
            ClearDirty();

            // Notify after store rebuilt.
            _service.NotifyRebuilt();

            if (debugLogs)
                Debug.Log($"[CrowSave.Flags][APPLY] reason={reason} rev={_service.Revision}", this);
        }

        public override void ResetState(ApplyReason reason)
        {
            base.ResetState(reason);

            if (reason != ApplyReason.DiskLoad)
                return;

            // Prevent slot contamination: clear before disk blob applies.
            _store.ClearAll();
            ClearDirty();

            // IMPORTANT: do not NotifyRebuilt here (would cause default-state projector pass pre-apply).
            if (debugLogs)
                Debug.Log($"[CrowSave.Flags][RESET] DiskLoad -> cleared all (no notify)", this);
        }
    }
}
