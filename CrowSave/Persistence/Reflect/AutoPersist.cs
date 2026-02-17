using CrowSave.Persistence.Runtime;
using UnityEngine;

namespace CrowSave.Persistence.Reflect
{
    /// <summary>
    /// Editor-time helper: ensures this GameObject has PersistentId + ReflectivePersistentProxy,
    /// and wires proxy.target to a chosen component.
    ///
    /// Mark [Persist] fields on the target, and optionally hooks.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class AutoPersist : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour target;

        [Header("Create / Wire")]
        [SerializeField] private bool ensurePersistentId = true;
        [SerializeField] private bool ensureProxy = true;

        [Tooltip("If true, auto-generate an EntityId in the editor if missing.")]
        [SerializeField] private bool generateIdIfMissing = true;

        public MonoBehaviour Target
        {
            get => target;
            set { target = value; Sync(); }
        }

        private void Reset() => Sync();
        private void OnValidate() => Sync();

        private void Sync()
        {
#if UNITY_EDITOR
            if (!gameObject) return;

            if (ensurePersistentId)
            {
                var id = GetComponent<PersistentId>();
                if (!id) id = gameObject.AddComponent<PersistentId>();

                if (generateIdIfMissing)
                    id.EditorGenerateIfMissing();
            }

            if (ensureProxy)
            {
                var proxy = GetComponent<ReflectivePersistentProxy>();
                if (!proxy) proxy = gameObject.AddComponent<ReflectivePersistentProxy>();

                // Best-effort: if target not assigned, default to "some other component on this GO".
                if (!target)
                    target = FindDefaultTarget();
                TryAssignProxyTarget(proxy, target);
            }
#endif
        }

#if UNITY_EDITOR
        private MonoBehaviour FindDefaultTarget()
        {
            // pick first non-AutoPersist, non-PersistentId, non-proxy component
            var mbs = GetComponents<MonoBehaviour>();
            for (int i = 0; i < mbs.Length; i++)
            {
                var mb = mbs[i];
                if (!mb) continue;
                if (mb == this) continue;
                if (mb is PersistentId) continue;
                if (mb is ReflectivePersistentProxy) continue;
                return mb;
            }
            return null;
        }

        private static void TryAssignProxyTarget(ReflectivePersistentProxy proxy, MonoBehaviour mb)
        {
            if (!proxy) return;

            // Use SerializedObject so Unity records the change properly in editor.
            var so = new UnityEditor.SerializedObject(proxy);
            var p = so.FindProperty("target");
            if (p != null)
            {
                p.objectReferenceValue = mb;
                so.ApplyModifiedPropertiesWithoutUndo();
                UnityEditor.EditorUtility.SetDirty(proxy);
            }
        }
#endif
    }
}
