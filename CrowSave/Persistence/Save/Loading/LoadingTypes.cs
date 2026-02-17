namespace CrowSave.Persistence.Save.Loading
{
    public enum LoadingOperation
    {
        Save = 0,
        Load = 1,
        Transition = 2,
        Checkpoint = 3
    }

    public enum LoadingViewMode
    {
        None = 0,           // No UI
        Indeterminate = 1,  // Spinner/text only (no bar)
        ProgressBar = 2,    // Bar + status text
        Steps = 3,          // Step list + status
        BarAndSteps = 4     // Bar + step list + status
    }

    public enum LoadingProgressSource
    {
        PipelineWeighted = 0, // Use orchestrator pipeline progress
        SceneAsyncOnly = 1,   // Use Unity AsyncOperation.progress (when available)
        Indeterminate = 2     // Always show as indeterminate/spinner
    }

    public readonly struct LoadingContext
    {
        public readonly LoadingOperation Operation;
        public readonly int Slot;
        public readonly string FromScene;
        public readonly string ToScene;
        public readonly bool Travel;

        public LoadingContext(
            LoadingOperation op,
            int slot = -1,
            string fromScene = null,
            string toScene = null,
            bool travel = false)
        {
            Operation = op;
            Slot = slot;
            FromScene = fromScene;
            ToScene = toScene;
            Travel = travel;
        }
    }

    public interface ILoadingScreen
    {
        void Show(LoadingContext ctx, LoadingViewMode viewMode);
        void Hide();

        void SetStatus(string text);
        void SetProgress(float value01); // ignored if Indeterminate

        // Optional extras (screen can ignore if not supported)
        void SetSteps(string[] steps, int activeIndex);
    }
}
