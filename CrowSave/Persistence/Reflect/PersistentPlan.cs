using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CrowSave.Persistence.Core;
using UnityEngine;

namespace CrowSave.Persistence.Reflect
{
    /// <summary>
    /// Builds a reflective plan for a type and can capture/apply [Persist] members.
    /// Format: header + (key, typecode, payloadbytes) repeated.
    /// Sorted keys make apply forward-skippable and allow defaulting missing members.
    /// </summary>
    public sealed class PersistentPlan
    {
        private const int Magic = unchecked((int)0x52464C31); // "RFL1"
        private const int FormatVersion = 1;

        private static readonly object _lock = new object();
        private static readonly Dictionary<Type, PersistentPlan> _cache = new Dictionary<Type, PersistentPlan>();

        private readonly Type _type;
        private readonly List<MemberPlan> _members; // sorted by Key asc

        private PersistentPlan(Type t, List<MemberPlan> members)
        {
            _type = t;
            _members = members ?? new List<MemberPlan>(0);
        }

        public static PersistentPlan ForType(Type t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));

            lock (_lock)
            {
                if (_cache.TryGetValue(t, out var p))
                    return p;

                var built = Build(t);
                _cache[t] = built;
                return built;
            }
        }

        public void Capture(object target, IStateWriter w)
        {
            if (target == null || w == null) return;

            w.WriteInt(Magic);
            w.WriteInt(FormatVersion);
            w.WriteInt(_members.Count);

            using var scratch = new ByteBuffer(128);

            for (int i = 0; i < _members.Count; i++)
            {
                var m = _members[i];

                WriteU64As2I32(w, m.Key);
                w.WriteInt((int)m.Code);

                scratch.Reset();
                m.WritePayload(target, scratch);
                w.WriteBytes(scratch.ToArray());
            }
        }

        public void Apply(object target, IStateReader r, ApplyReason reason, bool resetMissingOnDiskLoad)
        {
            if (target == null || r == null) return;

            int magic = r.ReadInt();
            if (magic != Magic)
            {
                Debug.LogWarning($"PersistentPlan({_type.Name}): magic mismatch (got {magic:X8}). Skipping apply.");
                return;
            }

            int ver = r.ReadInt();
            if (ver != FormatVersion)
                Debug.LogWarning($"PersistentPlan({_type.Name}): format {ver} != {FormatVersion}. Best-effort apply.");

            int count = r.ReadInt();
            if (count < 0) return;

            int mi = 0;
            int mcount = _members.Count;

            for (int i = 0; i < count; i++)
            {
                ulong blobKey = ReadU64From2I32(r);
                var blobCode = (ReflectTypeCode)r.ReadInt();
                byte[] payload = r.ReadBytes(); // always consume

                while (mi < mcount && _members[mi].Key < blobKey)
                {
                    if (resetMissingOnDiskLoad && reason == ApplyReason.DiskLoad)
                        _members[mi].ResetToDefault(target);
                    mi++;
                }

                if (mi >= mcount)
                    continue;

                var m = _members[mi];

                if (m.Key != blobKey)
                    continue;

                if (m.Code != blobCode)
                {
                    Debug.LogWarning($"PersistentPlan({_type.Name}): type mismatch for '{m.Name}'. saved={blobCode} now={m.Code}. Skipping.");
                    mi++;
                    continue;
                }

                try
                {
                    var br = new ByteBufferReader(payload);
                    m.ReadAndApply(target, ref br);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"PersistentPlan({_type.Name}): apply failed for '{m.Name}': {ex.Message}");
                }

                mi++;
            }

            if (resetMissingOnDiskLoad && reason == ApplyReason.DiskLoad)
            {
                while (mi < mcount)
                {
                    _members[mi].ResetToDefault(target);
                    mi++;
                }
            }
        }

        // --------------------------------------------------------------------
        // Build (OPT-IN ONLY)
        // --------------------------------------------------------------------

        private static PersistentPlan Build(Type t)
        {
            var members = new List<MemberPlan>(32);

            for (var cur = t; cur != null && cur != typeof(MonoBehaviour); cur = cur.BaseType)
            {
                const BindingFlags BF =
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

                foreach (var f in cur.GetFields(BF))
                {
                    if (!IsPersistOptIn(f)) continue;
                    if (Attribute.IsDefined(f, typeof(PersistIgnoreAttribute), inherit: true)) continue;
                    if (!TryGetCodeForType(f.FieldType, out var code, out var auxType)) continue;

                    ulong key = ComputeMemberKey(cur, f, f.Name);
                    members.Add(MemberPlan.ForField(key, f.Name, f, code, auxType));
                }

                foreach (var p in cur.GetProperties(BF))
                {
                    if (!IsPersistOptIn(p)) continue;
                    if (Attribute.IsDefined(p, typeof(PersistIgnoreAttribute), inherit: true)) continue;

                    if (!p.CanRead || !p.CanWrite) continue;
                    if (p.GetIndexParameters().Length != 0) continue;

                    if (!TryGetCodeForType(p.PropertyType, out var code, out var auxType)) continue;

                    ulong key = ComputeMemberKey(cur, p, p.Name);
                    members.Add(MemberPlan.ForProperty(key, p.Name, p, code, auxType));
                }
            }

            members.Sort((a, b) => a.Key.CompareTo(b.Key));
            return new PersistentPlan(t, members);
        }

        private static bool IsPersistOptIn(MemberInfo m)
        {
            if (m == null) return false;
            return Attribute.IsDefined(m, typeof(PersistAttribute), inherit: true)
                || Attribute.IsDefined(m, typeof(PersistKeyAttribute), inherit: true);
        }

        private static bool TryGetCodeForType(Type t, out ReflectTypeCode code, out Type auxType)
        {
            code = 0;
            auxType = null;

            if (t == null) return false;

            // never persist UnityEngine.Object refs via reflection
            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                return false;

            // Enums -> stored as int
            if (t.IsEnum)
            {
                code = ReflectTypeCode.EnumInt;
                auxType = t;
                return true;
            }

            if (t == typeof(int))   { code = ReflectTypeCode.Int; return true; }
            if (t == typeof(float)) { code = ReflectTypeCode.Float; return true; }
            if (t == typeof(bool))  { code = ReflectTypeCode.Bool; return true; }

            if (t == typeof(string)) { code = ReflectTypeCode.StringOptional; return true; }
            if (t == typeof(byte[])) { code = ReflectTypeCode.BytesOptional;  return true; }

            if (t == typeof(Vector3))    { code = ReflectTypeCode.Vector3;    return true; }
            if (t == typeof(Quaternion)) { code = ReflectTypeCode.Quaternion; return true; }

            // Nullable<Vector3/Quaternion>
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                var u = Nullable.GetUnderlyingType(t);
                if (u == typeof(Vector3))    { code = ReflectTypeCode.NullableVector3; return true; }
                if (u == typeof(Quaternion)) { code = ReflectTypeCode.NullableQuaternion; return true; }
            }

            // List<T> primitives
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var e = t.GetGenericArguments()[0];
                if (e == typeof(int))     { code = ReflectTypeCode.IntList; return true; }
                if (e == typeof(float))   { code = ReflectTypeCode.FloatList; return true; }
                if (e == typeof(bool))    { code = ReflectTypeCode.BoolList; return true; }
                if (e == typeof(string))  { code = ReflectTypeCode.StringList; return true; }
                if (e == typeof(Vector3)) { code = ReflectTypeCode.Vector3List; return true; }
            }

            return false;
        }

        // --------------------------------------------------------------------
        // MemberPlan
        // --------------------------------------------------------------------

        private sealed class MemberPlan
        {
            public readonly ulong Key;
            public readonly string Name;
            public readonly ReflectTypeCode Code;

            private readonly FieldInfo _field;
            private readonly PropertyInfo _prop;

            // enumType if Code==EnumInt, otherwise null
            private readonly Type _enumType;

            private MemberPlan(ulong key, string name, FieldInfo field, PropertyInfo prop, ReflectTypeCode code, Type enumType)
            {
                Key = key;
                Name = name;
                _field = field;
                _prop = prop;
                Code = code;
                _enumType = enumType;
            }

            public static MemberPlan ForField(ulong key, string name, FieldInfo f, ReflectTypeCode code, Type auxType)
                => new MemberPlan(key, name, f, null, code, auxType);

            public static MemberPlan ForProperty(ulong key, string name, PropertyInfo p, ReflectTypeCode code, Type auxType)
                => new MemberPlan(key, name, null, p, code, auxType);

            public void WritePayload(object target, IStateWriter w)
            {
                object v = GetValue(target);

                switch (Code)
                {
                    case ReflectTypeCode.Int:
                        w.WriteInt(v is int iv ? iv : 0);
                        break;

                    case ReflectTypeCode.Float:
                        w.WriteFloat(v is float fv ? fv : 0f);
                        break;

                    case ReflectTypeCode.Bool:
                        w.WriteBool(v is bool bv && bv);
                        break;

                    case ReflectTypeCode.StringOptional:
                        w.WriteString(v as string);
                        break;

                    case ReflectTypeCode.BytesOptional:
                        w.WriteBytes(v as byte[]);
                        break;

                    case ReflectTypeCode.Vector3:
                        w.WriteVector3(v is Vector3 vv ? vv : default);
                        break;

                    case ReflectTypeCode.Quaternion:
                        w.WriteQuaternion(v is Quaternion qq ? qq : default);
                        break;

                    case ReflectTypeCode.NullableVector3:
                    {
                        if (v is Vector3 v3) { w.WriteBool(true); w.WriteVector3(v3); }
                        else { w.WriteBool(false); }
                        break;
                    }

                    case ReflectTypeCode.NullableQuaternion:
                    {
                        if (v is Quaternion q4) { w.WriteBool(true); w.WriteQuaternion(q4); }
                        else { w.WriteBool(false); }
                        break;
                    }

                    case ReflectTypeCode.EnumInt:
                        w.WriteInt(v != null ? Convert.ToInt32(v) : 0);
                        break;

                    case ReflectTypeCode.IntList:
                        WriteListInt(w, v as List<int>);
                        break;

                    case ReflectTypeCode.FloatList:
                        WriteListFloat(w, v as List<float>);
                        break;

                    case ReflectTypeCode.BoolList:
                        WriteListBool(w, v as List<bool>);
                        break;

                    case ReflectTypeCode.StringList:
                        WriteListString(w, v as List<string>);
                        break;

                    case ReflectTypeCode.Vector3List:
                        WriteListVector3(w, v as List<Vector3>);
                        break;
                }
            }

            public void ReadAndApply(object target, ref ByteBufferReader r)
            {
                switch (Code)
                {
                    case ReflectTypeCode.Int:
                        SetValue(target, r.ReadInt());
                        break;

                    case ReflectTypeCode.Float:
                        SetValue(target, r.ReadFloat());
                        break;

                    case ReflectTypeCode.Bool:
                        SetValue(target, r.ReadBool());
                        break;

                    case ReflectTypeCode.StringOptional:
                        SetValue(target, r.ReadString());
                        break;

                    case ReflectTypeCode.BytesOptional:
                        SetValue(target, r.ReadBytes());
                        break;

                    case ReflectTypeCode.Vector3:
                        SetValue(target, r.ReadVector3());
                        break;

                    case ReflectTypeCode.Quaternion:
                        SetValue(target, r.ReadQuaternion());
                        break;

                    case ReflectTypeCode.NullableVector3:
                    {
                        bool has = r.ReadBool();
                        SetValue(target, has ? (Vector3?)r.ReadVector3() : null);
                        break;
                    }

                    case ReflectTypeCode.NullableQuaternion:
                    {
                        bool has = r.ReadBool();
                        SetValue(target, has ? (Quaternion?)r.ReadQuaternion() : null);
                        break;
                    }

                    case ReflectTypeCode.EnumInt:
                    {
                        int raw = r.ReadInt();
                        object ev = _enumType != null ? Enum.ToObject(_enumType, raw) : raw;
                        SetValue(target, ev);
                        break;
                    }

                    case ReflectTypeCode.IntList:
                        SetValue(target, ReadListInt(ref r));
                        break;

                    case ReflectTypeCode.FloatList:
                        SetValue(target, ReadListFloat(ref r));
                        break;

                    case ReflectTypeCode.BoolList:
                        SetValue(target, ReadListBool(ref r));
                        break;

                    case ReflectTypeCode.StringList:
                        SetValue(target, ReadListString(ref r));
                        break;

                    case ReflectTypeCode.Vector3List:
                        SetValue(target, ReadListVector3(ref r));
                        break;
                }
            }

            public void ResetToDefault(object target)
            {
                var t = GetMemberType();

                if (Nullable.GetUnderlyingType(t) != null)
                {
                    SetValue(target, null);
                    return;
                }

                object def = t.IsValueType ? Activator.CreateInstance(t) : null;
                SetValue(target, def);
            }

            private Type GetMemberType()
            {
                if (_field != null) return _field.FieldType;
                if (_prop != null) return _prop.PropertyType;
                return typeof(object);
            }

            private object GetValue(object target)
            {
                if (_field != null) return _field.GetValue(target);
                if (_prop != null) return _prop.GetValue(target, null);
                return null;
            }

            private void SetValue(object target, object value)
            {
                try
                {
                    if (_field != null) { _field.SetValue(target, value); return; }
                    if (_prop != null) _prop.SetValue(target, value, null);
                }
                catch { /* best-effort */ }
            }

            // ---- list helpers (count = -1 means null) ----

            private static void WriteListInt(IStateWriter w, List<int> list)
            {
                if (list == null) { w.WriteInt(-1); return; }
                w.WriteInt(list.Count);
                for (int i = 0; i < list.Count; i++) w.WriteInt(list[i]);
            }

            private static void WriteListFloat(IStateWriter w, List<float> list)
            {
                if (list == null) { w.WriteInt(-1); return; }
                w.WriteInt(list.Count);
                for (int i = 0; i < list.Count; i++) w.WriteFloat(list[i]);
            }

            private static void WriteListBool(IStateWriter w, List<bool> list)
            {
                if (list == null) { w.WriteInt(-1); return; }
                w.WriteInt(list.Count);
                for (int i = 0; i < list.Count; i++) w.WriteBool(list[i]);
            }

            private static void WriteListString(IStateWriter w, List<string> list)
            {
                if (list == null) { w.WriteInt(-1); return; }
                w.WriteInt(list.Count);
                for (int i = 0; i < list.Count; i++) w.WriteString(list[i]);
            }

            private static void WriteListVector3(IStateWriter w, List<Vector3> list)
            {
                if (list == null) { w.WriteInt(-1); return; }
                w.WriteInt(list.Count);
                for (int i = 0; i < list.Count; i++) w.WriteVector3(list[i]);
            }

            private static List<int> ReadListInt(ref ByteBufferReader r)
            {
                int n = r.ReadInt();
                if (n < 0) return null;
                var list = new List<int>(n);
                for (int i = 0; i < n; i++) list.Add(r.ReadInt());
                return list;
            }

            private static List<float> ReadListFloat(ref ByteBufferReader r)
            {
                int n = r.ReadInt();
                if (n < 0) return null;
                var list = new List<float>(n);
                for (int i = 0; i < n; i++) list.Add(r.ReadFloat());
                return list;
            }

            private static List<bool> ReadListBool(ref ByteBufferReader r)
            {
                int n = r.ReadInt();
                if (n < 0) return null;
                var list = new List<bool>(n);
                for (int i = 0; i < n; i++) list.Add(r.ReadBool());
                return list;
            }

            private static List<string> ReadListString(ref ByteBufferReader r)
            {
                int n = r.ReadInt();
                if (n < 0) return null;
                var list = new List<string>(n);
                for (int i = 0; i < n; i++) list.Add(r.ReadString());
                return list;
            }

            private static List<Vector3> ReadListVector3(ref ByteBufferReader r)
            {
                int n = r.ReadInt();
                if (n < 0) return null;
                var list = new List<Vector3>(n);
                for (int i = 0; i < n; i++) list.Add(r.ReadVector3());
                return list;
            }
        }

        // --------------------------------------------------------------------
        // Stable key hashing (FNV-1a 64 over UTF-16 chars, stable & allocation-free)
        // --------------------------------------------------------------------

        private static ulong ComputeMemberKey(Type declaringType, MemberInfo member, string memberName)
        {
            var pk = member.GetCustomAttribute<PersistKeyAttribute>(inherit: true);
            if (pk != null && !string.IsNullOrWhiteSpace(pk.Key))
                return Fnv1A64(pk.Key);

            var p = member.GetCustomAttribute<PersistAttribute>(inherit: true);
            if (p != null && !string.IsNullOrWhiteSpace(p.Key))
                return Fnv1A64(p.Key);

            string s = (declaringType != null ? declaringType.FullName : "(null)") + "." + (memberName ?? "");
            return Fnv1A64(s);
        }

        private static ulong Fnv1A64(string s)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime  = 1099511628211UL;

            ulong h = offset;
            if (s != null)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    h ^= (byte)(c & 0xFF); h *= prime;
                    h ^= (byte)((c >> 8) & 0xFF); h *= prime;
                }
            }
            return h;
        }

        private static void WriteU64As2I32(IStateWriter w, ulong v)
        {
            unchecked
            {
                w.WriteInt((int)(v & 0xFFFFFFFFUL));
                w.WriteInt((int)((v >> 32) & 0xFFFFFFFFUL));
            }
        }

        private static ulong ReadU64From2I32(IStateReader r)
        {
            unchecked
            {
                uint lo = (uint)r.ReadInt();
                uint hi = (uint)r.ReadInt();
                return ((ulong)hi << 32) | lo;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            lock (_lock) { _cache.Clear(); }
        }
    }
}
