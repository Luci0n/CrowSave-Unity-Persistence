namespace CrowSave.Persistence.Core
{
    public enum CaptureIntent : byte
    {
        Ram = 0,
        DiskManualSave = 1,
        DiskAutosave = 2,
        DiskCheckpoint = 3
    }
}
