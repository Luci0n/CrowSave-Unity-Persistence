using System.Collections.Generic;
using System.Linq;

namespace CrowSave.Persistence.Save.Pipeline
{
    /// Runs a sequence of weighted tasks, exposing current task name and overall progress.
    public sealed class LoadPipeline
    {
        private readonly List<WeightedTask> _tasks = new List<WeightedTask>();
        private int _index = -1;

        public string CurrentTaskName { get; private set; } = "(none)";
        public float OverallProgress { get; private set; } = 0f;
        public bool IsDone { get; private set; } = false;

        public IReadOnlyList<WeightedTask> Tasks => _tasks;

        public void Add(ILoadTask task, float weight = 1f)
        {
            _tasks.Add(new WeightedTask(task, weight));
        }

        public void Reset()
        {
            _index = -1;
            CurrentTaskName = "(none)";
            OverallProgress = 0f;
            IsDone = false;
        }

        public void Begin()
        {
            Reset();
            if (_tasks.Count == 0)
            {
                IsDone = true;
                OverallProgress = 1f;
                return;
            }

            _index = 0;
            _tasks[_index].Task.Begin();
            CurrentTaskName = _tasks[_index].Task.Name;
        }

        public void Tick()
        {
            if (IsDone) return;
            if (_tasks.Count == 0)
            {
                IsDone = true;
                OverallProgress = 1f;
                return;
            }

            var current = _tasks[_index].Task;
            CurrentTaskName = current.Name;

            current.Tick();

            // Update overall progress
            OverallProgress = ComputeOverallProgress();

            // Advance if done
            if (current.IsDone)
            {
                _index++;
                if (_index >= _tasks.Count)
                {
                    IsDone = true;
                    CurrentTaskName = "(done)";
                    OverallProgress = 1f;
                }
                else
                {
                    _tasks[_index].Task.Begin();
                    CurrentTaskName = _tasks[_index].Task.Name;
                }
            }
        }

        private float ComputeOverallProgress()
        {
            float totalW = _tasks.Sum(t => t.Weight);
            if (totalW <= 0f) return 1f;

            float doneW = 0f;
            float partialW = 0f;

            for (int i = 0; i < _tasks.Count; i++)
            {
                var wt = _tasks[i];
                if (i < _index)
                {
                    doneW += wt.Weight;
                }
                else if (i == _index)
                {
                    partialW += wt.Weight * Clamp01(wt.Task.Progress);
                }
                // tasks after current contribute 0
            }

            return Clamp01((doneW + partialW) / totalW);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        public readonly struct WeightedTask
        {
            public readonly ILoadTask Task;
            public readonly float Weight;

            public WeightedTask(ILoadTask task, float weight)
            {
                Task = task;
                Weight = weight <= 0f ? 0.0001f : weight;
            }
        }
    }
}
