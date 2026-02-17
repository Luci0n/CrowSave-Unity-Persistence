namespace CrowSave.Persistence.Save
{
    public enum SaveKind : byte
    {
        Unknown = 0,
        Manual = 1,
        Autosave = 2,
        Checkpoint = 3
    }
}
