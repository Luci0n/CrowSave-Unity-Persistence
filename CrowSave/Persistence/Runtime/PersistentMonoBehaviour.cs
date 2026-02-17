using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CrowSave.Persistence.Core;
using UnityEngine;

namespace CrowSave.Persistence.Runtime
{
    public abstract class PersistentMonoBehaviour : MonoBehaviour, IPersistentEntity, IDirtyPersistent, IResettablePersistent
    {
        [Header("Persistence")]

        [Tooltip(
            "How this entity participates in persistence:\n" +
            "- Never: not persisted; any runtime changes are ignored.\n" +
            "- SessionOnly: RAM only (keeps state while game runs; lost on quit).\n" +
            "- SaveGame: included in manual saves (disk).\n" +
            "- CheckpointOnly: included only when a checkpoint is written.\n" +
            "- Respawnable: treated as persistent but intended for future respawn/reset rules."
        )]
        [SerializeField] private PersistencePolicy policy = PersistencePolicy.SaveGame;

        [Tooltip("Apply order. Lower first. Use this if one entity depends on another being applied earlier.")]
        [SerializeField] private int priority = 0;

        [Tooltip(
            "Reset behavior during ApplyReason.DiskLoad:\n" +
            "- Keep: never auto-reset.\n" +
            "- ResetOnMissingOnDiskLoad: only reset if this entity is missing from the loaded save.\n" +
            "- ResetAlwaysOnDiskLoad: always reset on disk load and ignore any saved blob for this entity."
        )]
        [SerializeField] private ResetPolicy resetPolicy = ResetPolicy.Keep;

        [Tooltip(
            "If enabled, captures this component's serialized defaults at Awake and can restore them on ResetState().\n" +
            "Tip: defaults are whatever the inspector has at play start."
        )]
        [SerializeField] private bool autoResetFromDefaults = true;

        private string _defaultsJson;

        // Preserve top-level UnityEngine.Object serialized fields (and arrays/lists of them)
        // so JsonUtility.FromJsonOverwrite doesn't wipe inspector references to null.
        private List<UnityObjectFieldSnapshot> _unityObjectDefaults;

        private bool _dirty = true;
        private bool _started;
        private PersistentId _id;

        public PersistencePolicy Policy => policy;
        public int Priority => priority;
        public ResetPolicy ResetPolicy => resetPolicy;

        public bool IsDirty => _dirty;
        public void ClearDirty() => _dirty = false;

        protected void MarkDirty() => _dirty = true;

        protected virtual void Awake()
        {
            _id = GetComponent<PersistentId>();
            if (_id == null)
                Debug.LogWarning($"{name} has PersistentMonoBehaviour but no PersistentId component.", this);

            if (autoResetFromDefaults)
            {
                // Capture JSON defaults (value-like fields)
                _defaultsJson = JsonUtility.ToJson(this);

                // Capture UnityEngine.Object defaults separately (asset refs, etc.)
                _unityObjectDefaults = CaptureUnityObjectFieldDefaults();
            }
        }

        protected virtual void Start()
        {
            _started = true;
            TryRegister();
        }

        protected virtual void OnEnable()
        {
            if (_started)
                TryRegister();
        }

        protected virtual void OnDisable()
        {
            if (_id == null) return;
            if (!PersistenceServices.TryGet(out PersistenceRegistry registry)) return;
            registry.Unregister(_id);
        }

        private void TryRegister()
        {
            if (_id == null) return;

            if (!PersistenceServices.IsReady)
            {
                StartCoroutine(RegisterWhenReady());
                return;
            }

            var registry = PersistenceServices.Get<PersistenceRegistry>();
            registry.Register(_id, this);
        }

        private IEnumerator RegisterWhenReady()
        {
            while (!PersistenceServices.IsReady)
                yield return null;

            if (!this || !isActiveAndEnabled) yield break;
            if (_id == null || !_id.HasValidId) yield break;

            var registry = PersistenceServices.Get<PersistenceRegistry>();
            registry.Register(_id, this);
        }

        public virtual void ResetState(ApplyReason reason)
        {
            if (reason != ApplyReason.DiskLoad) return;

            if (resetPolicy == ResetPolicy.Keep) return;

            if (!autoResetFromDefaults || string.IsNullOrEmpty(_defaultsJson)) return;

            // Restore json defaults (value-like fields)
            JsonUtility.FromJsonOverwrite(_defaultsJson, this);

            // Restore UnityEngine.Object defaults (asset refs) that JsonUtility can wipe.
            if (_unityObjectDefaults != null && _unityObjectDefaults.Count > 0)
                RestoreUnityObjectFieldDefaults(_unityObjectDefaults);

            ClearDirty();
            OnAfterReset(reason);
        }

        /// <summary>
        /// Hook after defaults are restored. Use this to rebuild derived visuals/state.
        /// </summary>
        protected virtual void OnAfterReset(ApplyReason reason) { }

        public abstract void Capture(IStateWriter w);
        public abstract void Apply(IStateReader r);

        // UnityEngine.Object default preservation
        private struct UnityObjectFieldSnapshot
        {
            public FieldInfo Field;
            public SnapshotKind Kind;
            public UnityEngine.Object Single;
            public UnityEngine.Object[] Many;
            public Type ElementType;

            public enum SnapshotKind
            {
                Single = 0,
                Array = 1,
                List = 2
            }
        }

        private List<UnityObjectFieldSnapshot> CaptureUnityObjectFieldDefaults()
        {
            var list = new List<UnityObjectFieldSnapshot>(8);

            foreach (var f in EnumerateSerializedInstanceFields(GetType()))
            {
                var ft = f.FieldType;

                // Direct UnityEngine.Object ref
                if (typeof(UnityEngine.Object).IsAssignableFrom(ft))
                {
                    list.Add(new UnityObjectFieldSnapshot
                    {
                        Field = f,
                        Kind = UnityObjectFieldSnapshot.SnapshotKind.Single,
                        Single = f.GetValue(this) as UnityEngine.Object,
                        Many = null,
                        ElementType = ft
                    });
                    continue;
                }

                // UnityEngine.Object[]
                if (ft.IsArray)
                {
                    var elem = ft.GetElementType();
                    if (elem != null && typeof(UnityEngine.Object).IsAssignableFrom(elem))
                    {
                        var arr = f.GetValue(this) as Array;
                        UnityEngine.Object[] copy = null;

                        if (arr != null)
                        {
                            copy = new UnityEngine.Object[arr.Length];
                            for (int i = 0; i < arr.Length; i++)
                                copy[i] = arr.GetValue(i) as UnityEngine.Object;
                        }

                        list.Add(new UnityObjectFieldSnapshot
                        {
                            Field = f,
                            Kind = UnityObjectFieldSnapshot.SnapshotKind.Array,
                            Single = null,
                            Many = copy,
                            ElementType = elem
                        });
                        continue;
                    }
                }

                // List<T> where T : UnityEngine.Object
                if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var elem = ft.GetGenericArguments()[0];
                    if (typeof(UnityEngine.Object).IsAssignableFrom(elem))
                    {
                        var obj = f.GetValue(this);
                        UnityEngine.Object[] copy = null;

                        if (obj is System.Collections.IList ilist)
                        {
                            copy = new UnityEngine.Object[ilist.Count];
                            for (int i = 0; i < ilist.Count; i++)
                                copy[i] = ilist[i] as UnityEngine.Object;
                        }

                        list.Add(new UnityObjectFieldSnapshot
                        {
                            Field = f,
                            Kind = UnityObjectFieldSnapshot.SnapshotKind.List,
                            Single = null,
                            Many = copy,
                            ElementType = elem
                        });
                        continue;
                    }
                }
            }

            return list;
        }

        private void RestoreUnityObjectFieldDefaults(List<UnityObjectFieldSnapshot> snapshots)
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                var s = snapshots[i];
                if (s.Field == null) continue;

                try
                {
                    switch (s.Kind)
                    {
                        case UnityObjectFieldSnapshot.SnapshotKind.Single:
                            s.Field.SetValue(this, s.Single);
                            break;

                        case UnityObjectFieldSnapshot.SnapshotKind.Array:
                        {
                            if (s.ElementType == null) break;

                            if (s.Many == null)
                            {
                                s.Field.SetValue(this, null);
                                break;
                            }

                            var arr = Array.CreateInstance(s.ElementType, s.Many.Length);
                            for (int a = 0; a < s.Many.Length; a++)
                                arr.SetValue(s.Many[a], a);

                            s.Field.SetValue(this, arr);
                            break;
                        }

                        case UnityObjectFieldSnapshot.SnapshotKind.List:
                        {
                            // Unity serializes concrete List<T>, so we rebuild one.
                            if (s.ElementType == null) break;

                            if (s.Many == null)
                            {
                                s.Field.SetValue(this, null);
                                break;
                            }

                            var listType = typeof(List<>).MakeGenericType(s.ElementType);
                            var newList = (IList)Activator.CreateInstance(listType);

                            for (int a = 0; a < s.Many.Length; a++)
                                newList.Add(s.Many[a]);

                            s.Field.SetValue(this, newList);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"PersistentMonoBehaviour: Failed to restore UnityEngine.Object default for field '{s.Field.Name}' on '{name}': {ex.Message}",
                        this
                    );
                }
            }
        }

        private static IEnumerable<FieldInfo> EnumerateSerializedInstanceFields(Type type)
        {
            // Walk up the hierarchy so derived classes are included.
            // Stop at PersistentMonoBehaviour to avoid pulling in MonoBehaviour internals.
            for (var t = type; t != null && t != typeof(PersistentMonoBehaviour); t = t.BaseType)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var fields = t.GetFields(flags);

                for (int i = 0; i < fields.Length; i++)
                {
                    var f = fields[i];
                    if (f == null) continue;
                    if (f.IsStatic) continue;
                    if (f.IsInitOnly) continue; // readonly
                    if (Attribute.IsDefined(f, typeof(NonSerializedAttribute))) continue;

                    // Unity: public OR [SerializeField]
                    bool unitySerialized = f.IsPublic || Attribute.IsDefined(f, typeof(SerializeField));
                    if (!unitySerialized) continue;

                    yield return f;
                }
            }
        }

        /// <summary>
        /// Optional hook: copy runtime state into persisted fields before writing.
        /// Helper base classes may call this.
        /// </summary>
        protected virtual void Capture() { }

        /// <summary>
        /// Optional hook: rebuild runtime state after reading.
        /// Helper base classes may call this.
        /// </summary>
        protected virtual void Apply(ApplyReason reason) { }

        /// <summary>
        /// Simple public "I changed" signal for user scripts.
        /// (This just calls the existing protected MarkDirty()).
        /// </summary>
        public void NotifyChanged() => MarkDirty();
    }
}
