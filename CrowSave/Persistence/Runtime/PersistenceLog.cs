using System.Collections.Generic;
using UnityEngine;

namespace CrowSave.Persistence.Runtime
{
    public static class PersistenceLog
    {
        public static bool Enabled = true;

        private const int MaxEvents = 20;
        private static readonly Queue<string> _events = new Queue<string>(MaxEvents);

        public static string LastEvent { get; private set; } = "(none)";
        public static IReadOnlyCollection<string> Events => _events;

        public static void Info(string msg, Object ctx = null)
        {
            if (!Enabled) return;
            AddEvent(msg);
            Debug.Log($"[PERSIST] {msg}", ctx);
        }

        public static void Warn(string msg, Object ctx = null)
        {
            if (!Enabled) return;
            AddEvent("WARN: " + msg);
            Debug.LogWarning($"[PERSIST] {msg}", ctx);
        }

        public static void Error(string msg, Object ctx = null)
        {
            AddEvent("ERROR: " + msg);
            Debug.LogError($"[PERSIST] {msg}", ctx);
        }

        private static void AddEvent(string msg)
        {
            LastEvent = msg;
            if (_events.Count >= MaxEvents) _events.Dequeue();
            _events.Enqueue(msg);
        }

        public static void ClearEvents()
        {
            _events.Clear();
            LastEvent = "(none)";
        }
    }
}
