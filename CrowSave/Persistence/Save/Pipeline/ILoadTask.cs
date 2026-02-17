namespace CrowSave.Persistence.Save.Pipeline
{
    /// A single step in a load/save/transition pipeline.
    /// Progress must be [0..1], and IsDone becomes true when finished.
    public interface ILoadTask
    {
        string Name { get; }
        float Progress { get; }
        bool IsDone { get; }

        /// <summary>Called once before Tick is used.</summary>
        void Begin();

        /// <summary>Called every frame until IsDone=true.</summary>
        void Tick();
    }
}
