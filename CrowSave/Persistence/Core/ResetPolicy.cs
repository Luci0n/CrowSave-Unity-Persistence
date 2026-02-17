namespace CrowSave.Persistence.Core
{
    /// <summary>
    /// Controls how an entity should reset itself during ApplyReason.DiskLoad.
    ///
    /// Keep:
    /// - Never auto-reset during DiskLoad.
    ///
    /// ResetOnMissingOnDiskLoad:
    /// - Reset ONLY if the entity is missing from the loaded save blob set.
    ///
    /// ResetAlwaysOnDiskLoad:
    /// - Always reset on DiskLoad AND IGNORE any saved blob for this entity.
    ///   (The entity will be left in defaults for that disk load.)
    /// </summary>
    public enum ResetPolicy
    {
        /// <summary>Never auto-reset during DiskLoad.</summary>
        Keep = 0,

        /// <summary>
        /// Reset only when the entity does NOT exist in the loaded disk save for the scope.
        /// </summary>
        ResetOnMissingOnDiskLoad = 1,

        /// <summary>
        /// Always reset when DiskLoad happens, regardless of whether a blob exists.
        /// Any blob that exists is ignored for this entity on this DiskLoad.
        /// </summary>
        ResetAlwaysOnDiskLoad = 2
    }

    /// <summary>Reset behavior interface used by ApplyScope.</summary>
    public interface IResettablePersistent
    {
        ResetPolicy ResetPolicy { get; }
        void ResetState(ApplyReason reason);
    }
}
