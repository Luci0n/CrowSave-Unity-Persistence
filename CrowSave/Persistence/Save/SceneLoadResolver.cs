using System;
using CrowSave.Persistence.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using CrowSave.Persistence.Save.Pipeline;

namespace CrowSave.Persistence.Save
{
    /// Resolves how we LOAD scenes (travel/reload) based on SaveConfig.sceneIdentityMode.
    ///
    /// Key idea:
    /// - ActiveSceneId   = scope key (name:/path:/guid:) used for persistence data
    /// - ActiveSceneLoad = load key used for travel (can be name OR path OR guid)
    ///
    /// IMPORTANT for SceneGuid mode:
    /// - "guid:" values must be the *scene asset GUID* (AssetDatabase GUID),
    ///   which matches SceneGuidRegistry.sceneAssetGuid and SceneGuid.Guid.
    public static class SceneLoadResolver
    {
        private const string NamePrefix = "name:";
        private const string PathPrefix = "path:";
        private const string GuidPrefix = "guid:";

        private static SceneLoadRef InvalidLoadRef() => SceneLoadRef.FromBuildIndex(-1);

        public static string BuildActiveSceneLoadKey(Scene activeScene, SaveConfig cfg)
        {
            var mode = cfg != null ? cfg.sceneIdentityMode : SceneIdentityMode.SceneName;

            switch (mode)
            {
                case SceneIdentityMode.ScenePath:
                {
                    var path = activeScene.path ?? "";
                    if (!string.IsNullOrWhiteSpace(path))
                        return PathPrefix + path;

                    return NamePrefix + activeScene.name;
                }

                case SceneIdentityMode.SceneGuid:
                {
                    var sceneAssetGuid = TryGetSceneGuidInLoadedScene(activeScene);
                    if (!string.IsNullOrWhiteSpace(sceneAssetGuid))
                        return GuidPrefix + sceneAssetGuid;

                    return NamePrefix + activeScene.name;
                }

                case SceneIdentityMode.SceneName:
                default:
                    return NamePrefix + activeScene.name;
            }
        }

        public static SceneLoadRef ResolveLoadRef(SaveConfig cfg, string savedActiveSceneLoad)
        {
            savedActiveSceneLoad ??= "";

            if (!savedActiveSceneLoad.Contains(":"))
                return SceneLoadRef.FromName(savedActiveSceneLoad);

            if (savedActiveSceneLoad.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var n = savedActiveSceneLoad.Substring(NamePrefix.Length);
                return SceneLoadRef.FromName(n);
            }

            if (savedActiveSceneLoad.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var p = savedActiveSceneLoad.Substring(PathPrefix.Length);
                return SceneLoadRef.FromPath(p);
            }

            if (savedActiveSceneLoad.StartsWith(GuidPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var sceneAssetGuid = savedActiveSceneLoad.Substring(GuidPrefix.Length);

                if (TryResolveGuid(cfg, sceneAssetGuid, out var resolved))
                    return resolved;

                return InvalidLoadRef();
            }

            return SceneLoadRef.FromName(savedActiveSceneLoad);
        }

        public static SceneLoadRef ResolveLoadRefPreferGuid(SaveConfig cfg, string savedSceneId, string savedActiveSceneLoad)
        {
            var mode = cfg != null ? cfg.sceneIdentityMode : SceneIdentityMode.SceneName;

            if (mode == SceneIdentityMode.SceneGuid)
            {
                if (TryExtractGuidFromTypedId(savedSceneId, out var guidFromId))
                {
                    if (TryResolveGuid(cfg, guidFromId, out var resolved))
                        return resolved;
                    return InvalidLoadRef();
                }
            }

            return ResolveLoadRef(cfg, savedActiveSceneLoad);
        }

        private static bool TryResolveGuid(SaveConfig cfg, string sceneAssetGuid, out SceneLoadRef loadRef)
        {
            loadRef = default;

            if (string.IsNullOrWhiteSpace(sceneAssetGuid))
                return false;

            var registry = cfg != null ? cfg.sceneGuidRegistry : null;
            if (registry != null && registry.TryResolve(sceneAssetGuid, out var entry))
            {
                if (entry.buildIndex >= 0)
                {
                    loadRef = SceneLoadRef.FromBuildIndex(entry.buildIndex);
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(entry.scenePath))
                {
                    loadRef = SceneLoadRef.FromPath(entry.scenePath);
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(entry.sceneName))
                {
                    loadRef = SceneLoadRef.FromName(entry.sceneName);
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractGuidFromTypedId(string typedId, out string guid)
        {
            guid = "";
            typedId = SceneIdentity.NormalizeLegacy(typedId ?? "");

            if (typedId.StartsWith(GuidPrefix, StringComparison.OrdinalIgnoreCase))
            {
                guid = typedId.Substring(GuidPrefix.Length);
                return !string.IsNullOrWhiteSpace(guid);
            }

            int idx = typedId.IndexOf(GuidPrefix, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var sub = typedId.Substring(idx + GuidPrefix.Length);
                var cut = sub.IndexOfAny(new[] { '|', ' ', '\t', '\n', '\r' });
                guid = cut >= 0 ? sub.Substring(0, cut) : sub;
                return !string.IsNullOrWhiteSpace(guid);
            }

            return false;
        }

        private static string TryGetSceneGuidInLoadedScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return "";

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var sg = roots[i].GetComponentInChildren<SceneGuid>(true);
                if (sg != null && !string.IsNullOrWhiteSpace(sg.Guid))
                    return sg.Guid; // scene asset GUID
            }

            return "";
        }
    }
}
