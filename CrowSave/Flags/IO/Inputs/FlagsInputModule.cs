using System;
using UnityEngine;

namespace CrowSave.Flags.IO.Inputs
{
    [Serializable]
    public abstract class FlagsInputModule
    {
        [NonSerialized] public int __routeIndex = -1;

        [Header("Fire Once")]
        [SerializeField] private bool fireOnlyOnce = false;

        [Tooltip("If true, the Fire Once latch is stored in FlagsService, so it survives scene reloads / disk loads.")]
        [SerializeField] private bool persistFireOnce = true;

        [Tooltip("Channel used for the persistent latch in FlagsService.")]
        [SerializeField] private string fireOnceChannel = "__once";

        [Tooltip("If true, re-enables firing when the cause is disabled/enabled again (local-only latch).")]
        [SerializeField] private bool resetLocalFireOnceOnDisable = false;

        [NonSerialized] private bool _localFiredOnce;

        public virtual void OnAwake(FlagsIOCause host) { }
        public virtual void OnEnable(FlagsIOCause host) { }
        public virtual void OnStart(FlagsIOCause host) { }
        public virtual void OnUpdate(FlagsIOCause host) { }

        public virtual void OnDisable(FlagsIOCause host)
        {
            if (fireOnlyOnce && resetLocalFireOnceOnDisable)
                _localFiredOnce = false;
        }

        public virtual void OnTriggerEnter(FlagsIOCause host, Collider other) { }
        public virtual void OnTriggerExit(FlagsIOCause host, Collider other) { }

        protected bool Fire(FlagsIOCause host)
        {
            if (host == null) return false;

            if (!fireOnlyOnce)
                return host.FireRoute(__routeIndex);

            // Local-only latch
            if (!persistFireOnce)
            {
                if (_localFiredOnce) return false;

                bool fired = host.FireRoute(__routeIndex);
                if (fired) _localFiredOnce = true;
                return fired;
            }

            // Persistent latch (FlagsService)
            if (!host.EnsureBound())
                return false;

            string scope = host.GetScopeKey();
            string latchTargetKey = BuildFireOnceTargetKey(host);

            bool already = host.Flags.GetBool(scope, latchTargetKey, fireOnceChannel, fallback: false);
            if (already) return false;

            bool firedAny = host.FireRoute(__routeIndex);

            if (firedAny)
                host.Flags.SetBool(scope, latchTargetKey, fireOnceChannel, true);

            return firedAny;
        }

        private string BuildFireOnceTargetKey(FlagsIOCause host)
        {
            // Requires stable host.CauseId
            string causeId = string.IsNullOrWhiteSpace(host.CauseId) ? "missing" : host.CauseId;
            string inputType = GetType().Name;
            return $"once:{causeId}:{__routeIndex}:{inputType}";
        }
    }
}
