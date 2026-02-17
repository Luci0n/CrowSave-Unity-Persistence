using System.Collections;
using System.Collections.Generic;
using CrowSave.Flags.Core;
using CrowSave.Flags.Runtime;
using CrowSave.Persistence.Save;
using CrowSave.Persistence.Runtime;
using UnityEngine;

namespace CrowSave.Flags.IO
{
    [DisallowMultipleComponent]
    public sealed class FlagsIOCause : MonoBehaviour
    {
        public enum ScopeMode
        {
            Global = 0,
            CurrentScene = 1,
            CustomScopeKey = 2
        }

        [Header("Scope")]
        [SerializeField] private ScopeMode scopeMode = ScopeMode.CurrentScene;

        [SerializeField, Tooltip("Only used when ScopeMode = CustomScopeKey.")]
        private string customScopeKey = "";

        [Header("Identity")]
        [Tooltip("Stable identifier for this Cause. Used by persistent FireOnce latches (and other per-cause keys).")]
        [SerializeField] private string causeId = "";

        [Header("Binding")]
        [Tooltip("Optional. If null, will try FlagsStatePersistence.Instance or FindAnyObjectByType.")]
        [SerializeField] private FlagsStatePersistence flagsState;

        [SerializeField, Min(0)]
        private int bindMaxFrames = 30;

        [Header("Routes")]
        [SerializeField] private List<FlagsIORoute> routes = new List<FlagsIORoute>();

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private FlagsService _flags;
        private bool _ready;
        private Coroutine _bindRoutine;

        public string CauseId => causeId;
        public bool IsReady => _ready && _flags != null;
        public FlagsService Flags => _flags;

        private void Awake()
        {
            EnsureCauseId();
            ReindexRoutes();

            for (int i = 0; i < routes.Count; i++)
                routes[i]?.Input?.OnAwake(this);
        }

        private void OnEnable()
        {
            _bindRoutine = StartCoroutine(BindRoutine());

            for (int i = 0; i < routes.Count; i++)
                routes[i]?.Input?.OnEnable(this);
        }

        private void Start()
        {
            for (int i = 0; i < routes.Count; i++)
                routes[i]?.Input?.OnStart(this);
        }

        private void Update()
        {
            for (int i = 0; i < routes.Count; i++)
                routes[i]?.Input?.OnUpdate(this);
        }

        private void OnDisable()
        {
            if (_bindRoutine != null) StopCoroutine(_bindRoutine);
            _bindRoutine = null;

            for (int i = 0; i < routes.Count; i++)
                routes[i]?.Input?.OnDisable(this);
        }

        private void OnTriggerEnter(Collider other)
        {
            for (int i = 0; i < routes.Count; i++)
                routes[i]?.Input?.OnTriggerEnter(this, other);
        }

        private void OnTriggerExit(Collider other)
        {
            for (int i = 0; i < routes.Count; i++)
                routes[i]?.Input?.OnTriggerExit(this, other);
        }

        public bool EnsureBound()
        {
            if (_flags != null) return true;
            return TryBind();
        }

        private IEnumerator BindRoutine()
        {
            _ready = false;
            _flags = null;

            for (int i = 0; i <= bindMaxFrames; i++)
            {
                if (TryBind()) break;
                yield return null;
            }

            _ready = _flags != null;

            if (!_ready && debugLogs)
                Debug.LogWarning("[CrowSave.Flags][IOCause] No FlagsService bound.", this);
        }

        private bool TryBind()
        {
            if (_flags != null) return true;

            if (flagsState == null)
            {
                flagsState = FlagsStatePersistence.Instance != null
                    ? FlagsStatePersistence.Instance
                    : FindAnyObjectByType<FlagsStatePersistence>(FindObjectsInactive.Include);
            }

            if (flagsState == null) return false;

            _flags = flagsState.Service;
            return _flags != null;
        }

        private void ReindexRoutes()
        {
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (r?.Input != null)
                    r.Input.__routeIndex = i;
            }
        }

        private SceneIdentityMode GetSceneIdentityMode()
        {
            var orch = SaveOrchestrator.Instance;
            var cfg = orch != null ? orch.GetConfig() : null;
            return cfg != null ? cfg.sceneIdentityMode : SceneIdentityMode.SceneName;
        }

        public string GetScopeKey()
        {
            switch (scopeMode)
            {
                case ScopeMode.Global:
                    return "";

                case ScopeMode.CustomScopeKey:
                    return FlagsScope.Normalize(customScopeKey);

                case ScopeMode.CurrentScene:
                default:
                {
                    var mode = GetSceneIdentityMode();
                    var myScene = gameObject.scene;
                    var sceneScope = SceneIdentity.GetSceneId(myScene, mode);
                    return FlagsScope.Normalize(sceneScope);
                }
            }
        }

        public bool FireRoute(int routeIndex)
        {
            if (routeIndex < 0 || routeIndex >= routes.Count) return false;

            var route = routes[routeIndex];
            if (route == null) return false;

            if (!IsReady && !TryBind())
                return false;

            var outputs = route.Outputs;
            if (outputs == null || outputs.Count == 0) return false;

            string scope = GetScopeKey();

            if (debugLogs)
                Debug.Log($"[CrowSave.Flags][IOCause] FIRE route='{route.Name}' scope='{scope}'", this);

            bool firedAny = false;

            for (int i = 0; i < outputs.Count; i++)
            {
                var o = outputs[i];
                if (o == null) continue;
                o.Invoke(this, _flags, scope);
                firedAny = true;
            }

            return firedAny;
        }

        public void ApplyFromStore(FlagsService flags)
        {
            if (flags == null) return;

            string scope = GetScopeKey();

            if (debugLogs)
                Debug.Log($"[CrowSave.Flags][IOCause] RESTORE scope='{scope}'", this);

            _flags = flags;
            _ready = true;

            for (int ri = 0; ri < routes.Count; ri++)
            {
                var r = routes[ri];
                if (r == null) continue;

                var outputs = r.Outputs;
                if (outputs == null) continue;

                for (int oi = 0; oi < outputs.Count; oi++)
                {
                    var o = outputs[oi];
                    if (o == null) continue;
                    o.Restore(this, flags, scope);
                }
            }
        }

        private void EnsureCauseId()
        {
            if (!string.IsNullOrWhiteSpace(causeId)) return;
            causeId = System.Guid.NewGuid().ToString("N");
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureCauseId();
            ReindexRoutes();
        }
#endif
    }
}
