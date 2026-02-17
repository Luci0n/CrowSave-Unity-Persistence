namespace CrowSave.Persistence.Core
{
    public enum ApplyReason
    {
        Transition = 0,
        DiskLoad = 1,
        Checkpoint = 2,
        Respawn = 3
    }
}
