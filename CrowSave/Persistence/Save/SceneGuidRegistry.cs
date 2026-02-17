using System;
using System.Collections.Generic;
using UnityEngine;

namespace CrowSave.Persistence.Save
{
    /// Runtime registry mapping *Scene Asset GUID* (AssetDatabase GUID) -> enabled build index + path.
    /// IMPORTANT:
    /// - sceneAssetGuid must match what SceneGuid stores in each scene.
    /// - buildIndex here should match the ENABLED Build Settings order (0..N-1),
    ///   which is what matters for runtime loading by index in player builds.
    [CreateAssetMenu(menuName = "CrowSave/Scene GUID Registry", fileName = "SceneGuidRegistry")]
    public sealed class SceneGuidRegistry : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string sceneAssetGuid; // AssetDatabase GUID (32 hex)
            public int buildIndex;        // Enabled Build Settings index (0..N-1). -1 if not enabled/in build.
            public string scenePath;      // Assets/.../MyScene.unity (editor-facing path)
            public string sceneName;      // Debug only
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();
        public IReadOnlyList<Entry> Entries => entries;

        // Runtime cache (fast lookup, handles case-insensitivity)
        private Dictionary<string, int> _guidToIndex;

        private void OnEnable()
        {
            RebuildCache();
        }

#if UNITY_EDITOR
        public void EditorSet(List<Entry> newEntries)
        {
            // Copy to avoid reference-sharing surprises
            entries = newEntries != null ? new List<Entry>(newEntries) : new List<Entry>();
            RebuildCache();
        }

        public List<Entry> EditorGetEntries()
        {
            // Return a copy so callers can't mutate your internal list accidentally
            return entries != null ? new List<Entry>(entries) : new List<Entry>();
        }
#endif

        public bool TryResolve(string sceneAssetGuid, out Entry entry)
        {
            entry = default;

            if (string.IsNullOrWhiteSpace(sceneAssetGuid) || entries == null)
                return false;

            if (_guidToIndex == null)
                RebuildCache();

            if (_guidToIndex != null && _guidToIndex.TryGetValue(sceneAssetGuid, out int idx) && idx >= 0 && idx < entries.Count)
            {
                entry = entries[idx];
                return true;
            }

            // Fallback (should rarely hit)
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].sceneAssetGuid, sceneAssetGuid, StringComparison.OrdinalIgnoreCase))
                {
                    entry = entries[i];
                    return true;
                }
            }

            return false;
        }

        private void RebuildCache()
        {
            _guidToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (entries == null) return;

            for (int i = 0; i < entries.Count; i++)
            {
                var g = entries[i].sceneAssetGuid;
                if (string.IsNullOrWhiteSpace(g))
                    continue;

                // If duplicates exist, keep the FIRST and warn once per duplicate.
                if (_guidToIndex.ContainsKey(g))
                {
                    Debug.LogWarning($"[CrowSave] SceneGuidRegistry has duplicate GUID entry '{g}'. Keeping the first occurrence.", this);
                    continue;
                }

                _guidToIndex[g] = i;
            }
        }
    }
}
