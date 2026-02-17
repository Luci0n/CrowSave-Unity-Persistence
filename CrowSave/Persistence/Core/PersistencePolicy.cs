namespace CrowSave.Persistence.Core
{
    public enum PersistencePolicy
    {
        Never = 0,
        SessionOnly = 1,    // RAM only (lost on quit)
        SaveGame = 2,       // Included in disk saves
        CheckpointOnly = 3, // Only saved on checkpoint commits
        Respawnable = 4     // Subject to reset rules (later)
    }
}
