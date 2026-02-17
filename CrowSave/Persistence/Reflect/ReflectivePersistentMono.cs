using CrowSave.Persistence.Core;
using CrowSave.Persistence.Runtime;
using UnityEngine;

namespace CrowSave.Persistence.Reflect
{
    [DisallowMultipleComponent]
    public class ReflectivePersistentMono : PersistentMonoBehaviour, IApplyReasonedPersistent
    {
        [Header("Reflect")]
        [SerializeField] private bool resetMissingOnDiskLoad = false;

        public sealed override void Capture(IStateWriter w)
        {
            // user hook (optional overload on PersistentMonoBehaviour)
            Capture();

            var plan = PersistentPlan.ForType(GetType());
            plan.Capture(this, w);

            ClearDirty();
        }

        public sealed override void Apply(IStateReader r) => Apply(r, ApplyReason.Transition);

        // NOT an override -> cannot be sealed
        public void Apply(IStateReader r, ApplyReason reason)
        {
            var plan = PersistentPlan.ForType(GetType());
            plan.Apply(this, r, reason, resetMissingOnDiskLoad);

            // user hook (optional overload on PersistentMonoBehaviour)
            Apply(reason);

            ClearDirty();
        }
    }
}
