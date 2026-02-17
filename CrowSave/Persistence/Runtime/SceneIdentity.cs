using UnityEngine;
using UnityEngine.SceneManagement;
using CrowSave.Persistence.Save;

namespace CrowSave.Persistence.Runtime
{
    public static class SceneIdentity
    {
        private const string NamePrefix = "name:";
        private const string PathPrefix = "path:";
        private const string GuidPrefix = "guid:";

        public static string NormalizeLegacy(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return "";
            if (idOrName.Contains(":")) return idOrName; // already typed
            return NamePrefix + idOrName; // legacy v2/v3
        }

        public static string GetSceneId(Scene scene, SceneIdentityMode mode)
        {
            switch (mode)
            {
                case SceneIdentityMode.ScenePath:
                {
                    // In player builds, scene.path can be empty.
                    // Never emit "path:" (empty) because it becomes a dead scope key.
                    var path = scene.path;
                    if (!string.IsNullOrWhiteSpace(path))
                        return PathPrefix + path;

                    // Fallback to a stable-ish runtime identifier.
                    return NamePrefix + scene.name;
                }

                case SceneIdentityMode.SceneGuid:
                {
                    // Never emit "guid:" (empty). If GUID is missing, fall back to name.
                    var guid = GetSceneGuidOrEmpty(scene);
                    if (!string.IsNullOrWhiteSpace(guid))
                        return GuidPrefix + guid;

                    return NamePrefix + scene.name;
                }

                default:
                    return NamePrefix + scene.name;
            }
        }

        public static bool IsEmptyId(string typedId)
        {
            if (string.IsNullOrWhiteSpace(typedId)) return true;

            // Empty typed ids like "guid:" or "path:" or "name:" are invalid.
            // Also treat any trailing-colon token as empty.
            if (typedId.EndsWith(":", System.StringComparison.Ordinal)) return true;

            // Extra safety: if someone passes a prefix-only value with whitespace after colon.
            // e.g. "guid:   "
            int colon = typedId.IndexOf(':');
            if (colon >= 0 && colon == typedId.Length - 1) return true;

            return false;
        }

        public static string GetSceneGuidOrEmpty(Scene scene)
        {
            // Runtime-safe search: find SceneGuid component in this loaded scene.
            if (!scene.IsValid() || !scene.isLoaded)
                return "";

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var sg = roots[i].GetComponentInChildren<SceneGuid>(true);
                if (sg != null && !string.IsNullOrWhiteSpace(sg.Guid))
                    return sg.Guid;
            }
            return "";
        }
    }
}
