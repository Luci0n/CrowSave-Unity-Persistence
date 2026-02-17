using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace CrowSave.Persistence.Runtime
{
    /// <summary>
    /// Stores the *Scene Asset GUID* (AssetDatabase GUID) so it matches SceneGuidRegistry entries.
    /// This is stable across rename/move and is the correct identifier for SceneGuid identity mode.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneGuid : MonoBehaviour
    {
        // IMPORTANT: This must be the *scene asset GUID* (AssetDatabase GUID), NOT a random Guid.
        [SerializeField] private string guid;

        public string Guid => guid;

#if UNITY_EDITOR
        /// <summary>
        /// Ensures this component's GUID matches the *scene asset GUID* for the scene this object belongs to.
        /// </summary>
        public void EnsureGuid()
        {
            var scene = gameObject.scene;
            var path = scene.path;

            if (string.IsNullOrWhiteSpace(path))
            {
                Debug.LogWarning("SceneGuid.EnsureGuid: Scene has no path. Save the scene asset first.", this);
                return;
            }

            var assetGuid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrWhiteSpace(assetGuid))
            {
                Debug.LogWarning($"SceneGuid.EnsureGuid: Could not resolve AssetDatabase GUID for scene path '{path}'.", this);
                return;
            }

            if (!string.Equals(guid, assetGuid, StringComparison.OrdinalIgnoreCase))
            {
                guid = assetGuid;

                EditorUtility.SetDirty(this);
                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private void OnValidate()
        {
            // Auto-sync whenever something changes in the editor.
            EnsureGuid();
        }

        [ContextMenu("CrowSave/Sync Scene GUID From Scene Asset")]
        private void ContextSync() => EnsureGuid();
#endif
    }
}
