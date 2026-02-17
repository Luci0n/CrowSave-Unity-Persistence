namespace CrowSave.Persistence.Core
{
    public interface IPersistentEntity
    {
        PersistencePolicy Policy { get; }
        int Priority { get; }

        void Capture(IStateWriter w);

        // Legacy apply (kept for compatibility)
        void Apply(IStateReader r);
    }

    /// <summary>
    /// Optional: apply with reason. If implemented, the system will call this instead of Apply(r).
    /// Lets entities do different things on DiskLoad vs Transition.
    /// </summary>
    public interface IApplyReasonedPersistent
    {
        void Apply(IStateReader r, ApplyReason reason);
    }

    /// <summary>If implemented, CaptureDirty can skip non-dirty entities.</summary>
    public interface IDirtyPersistent
    {
        bool IsDirty { get; }
        void ClearDirty();
    }
}
