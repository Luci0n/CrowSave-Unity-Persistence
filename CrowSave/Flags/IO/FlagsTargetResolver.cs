using System;
using System.Collections.Generic;
using CrowSave.Flags.Core;
using CrowSave.Flags.IO;
using CrowSave.Persistence.Runtime;
using UnityEngine;

namespace CrowSave.Flags.Runtime
{
    /// <summary>
    /// Resolves FlagsTargetRef into live GameObjects in the currently loaded scenes.
    ///
    /// Identity priority:
    /// 1) PersistentId.EntityId (stable, intended for cross-session references)
    /// 2) FlagsLocalId.Id (scene-local convenience)
    /// 3) GameObject.name (fallback; not recommended)
    ///
    /// Notes:
    /// - PersistentId mode resolves by comparing against PersistentId.EntityId (string).
    /// - SceneNameFilter is a best-effort runtime filter against go.scene.name (only loaded scenes can match).
    /// </summary>
    public static class FlagsTargetResolver
    {
        public static void ResolveAll(in FlagsTargetRef tr, List<GameObject> results)
        {
            if (results == null) return;
            results.Clear();

            switch (tr.RefMode)
            {
                case FlagsTargetRef.Mode.DirectGameObject:
                    if (tr.Direct != null) results.Add(tr.Direct);
                    return;

                case FlagsTargetRef.Mode.PersistentIdComponent:
                    if (tr.PersistentId != null && tr.PersistentId.gameObject != null)
                        results.Add(tr.PersistentId.gameObject);
                    return;

                case FlagsTargetRef.Mode.LocalId:
                    ResolveByLocalIdAll(tr.LocalId, tr.SceneNameFilter, results);
                    return;

                case FlagsTargetRef.Mode.ObjectName:
                    ResolveByNameAll(tr.ObjectName, tr.SceneNameFilter, results);
                    return;

                case FlagsTargetRef.Mode.PersistentEntityIdString:
                    ResolveByPersistentEntityIdAll(tr.PersistentEntityId, tr.SceneNameFilter, results);
                    return;

                default:
                    return;
            }
        }

        public static string GetTargetKey(GameObject go)
        {
            if (go == null) return "";

            var pid = go.GetComponent<PersistentId>();
            if (pid != null && pid.HasValidId)
                return FlagsTargetKey.FromPersistentId(pid.EntityId);

            var lid = go.GetComponent<FlagsLocalId>();
            if (lid != null && !string.IsNullOrWhiteSpace(lid.Id))
                return FlagsTargetKey.FromLocalId(lid.Id);

            return FlagsTargetKey.FromName(go.name);
        }

        private static void ResolveByLocalIdAll(string id, string optionalScene, List<GameObject> results)
        {
            id = (id ?? "").Trim();
            if (id.Length == 0) return;

            var all = UnityEngine.Object.FindObjectsByType<FlagsLocalId>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null) continue;

                var go = c.gameObject;
                if (go == null) continue;
                if (!go.scene.IsValid() || !go.scene.isLoaded) continue;

                if (!string.IsNullOrWhiteSpace(optionalScene) &&
                    !string.Equals(go.scene.name, optionalScene, StringComparison.Ordinal))
                    continue;

                if (string.Equals(c.Id, id, StringComparison.Ordinal))
                    AddUnique(results, go);
            }
        }

        private static void ResolveByNameAll(string name, string optionalScene, List<GameObject> results)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;

            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                var go = all[i];
                if (go == null) continue;
                if (!go.scene.IsValid() || !go.scene.isLoaded) continue;

                if (!string.IsNullOrWhiteSpace(optionalScene) &&
                    !string.Equals(go.scene.name, optionalScene, StringComparison.Ordinal))
                    continue;

                if (string.Equals(go.name, name, StringComparison.Ordinal))
                    AddUnique(results, go);
            }
        }

        private static void ResolveByPersistentEntityIdAll(string entityId, string optionalScene, List<GameObject> results)
        {
            entityId = (entityId ?? "").Trim();
            if (entityId.Length == 0) return;

            var all = UnityEngine.Object.FindObjectsByType<PersistentId>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            for (int i = 0; i < all.Length; i++)
            {
                var pid = all[i];
                if (pid == null) continue;

                var go = pid.gameObject;
                if (go == null) continue;
                if (!go.scene.IsValid() || !go.scene.isLoaded) continue;

                if (!string.IsNullOrWhiteSpace(optionalScene) &&
                    !string.Equals(go.scene.name, optionalScene, StringComparison.Ordinal))
                    continue;

                if (!pid.HasValidId) continue;

                if (string.Equals(pid.EntityId, entityId, StringComparison.Ordinal))
                    AddUnique(results, go);
            }
        }

        private static void AddUnique(List<GameObject> list, GameObject go)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == go) return;
            }
            list.Add(go);
        }
    }
}
