using CrowSave.Persistence.Core;
using CrowSave.Persistence.Runtime;
using UnityEngine;

namespace CrowSave.Persistence.Reflect
{
    /// <summary>
    /// Persist any target MonoBehaviour by pointing this proxy at it.
    /// The target must have [Persist] fields/properties.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReflectivePersistentProxy : PersistentMonoBehaviour, IApplyReasonedPersistent
    {
        [Header("Target")]
        [SerializeField] private MonoBehaviour target;

        [Header("Reflect")]
        [SerializeField] private bool resetMissingOnDiskLoad = false;

        public override void Capture(IStateWriter w)
        {
            if (!target) return;

            // Optional: if the target also wants the "simple hooks" naming,
            // it can define methods with these signatures and we call them safely.
            TryInvokeNoArg(target, "Capture");

            var plan = PersistentPlan.ForType(target.GetType());
            plan.Capture(target, w);

            ClearDirty();
        }

        public override void Apply(IStateReader r) => Apply(r, ApplyReason.Transition);

        public void Apply(IStateReader r, ApplyReason reason)
        {
            if (!target) return;

            var plan = PersistentPlan.ForType(target.GetType());
            plan.Apply(target, r, reason, resetMissingOnDiskLoad);

            TryInvokeOneArg(target, "Apply", typeof(ApplyReason), reason);

            ClearDirty();
        }

        private static void TryInvokeNoArg(object obj, string method)
        {
            try
            {
                var t = obj.GetType();
                var m = t.GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, System.Type.EmptyTypes, null);
                m?.Invoke(obj, null);
            }
            catch { }
        }

        private static void TryInvokeOneArg(object obj, string method, System.Type argType, object arg)
        {
            try
            {
                var t = obj.GetType();
                var m = t.GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { argType }, null);
                m?.Invoke(obj, new[] { arg });
            }
            catch { }
        }
    }
}
