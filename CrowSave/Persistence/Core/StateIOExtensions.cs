using System;
using System.Collections.Generic;
using UnityEngine;

namespace CrowSave.Persistence.Core
{
    /// <summary>
    /// Convenience helpers on top of IStateWriter/IStateReader.
    /// Keeps entity Capture/Apply code small and consistent.
    /// </summary>
    public static class StateIOExtensions
    {
        // Optional primitives
        public static void WriteOptionalString(this IStateWriter w, string v)
        {
            bool has = !string.IsNullOrEmpty(v);
            w.WriteBool(has);
            if (has) w.WriteString(v);
        }

        public static string ReadOptionalString(this IStateReader r)
        {
            bool has = r.ReadBool();
            return has ? r.ReadString() : null;
        }

        public static void WriteOptionalBytes(this IStateWriter w, byte[] v)
        {
            bool has = v != null;
            w.WriteBool(has);
            if (has) w.WriteBytes(v);
        }

        public static byte[] ReadOptionalBytes(this IStateReader r)
        {
            bool has = r.ReadBool();
            return has ? r.ReadBytes() : null;
        }

        public static void WriteOptionalVector3(this IStateWriter w, Vector3? v)
        {
            bool has = v.HasValue;
            w.WriteBool(has);
            if (has) w.WriteVector3(v.Value);
        }

        public static Vector3? ReadOptionalVector3(this IStateReader r)
        {
            bool has = r.ReadBool();
            return has ? r.ReadVector3() : null;
        }

        public static void WriteOptionalQuaternion(this IStateWriter w, Quaternion? q)
        {
            bool has = q.HasValue;
            w.WriteBool(has);
            if (has) w.WriteQuaternion(q.Value);
        }

        public static Quaternion? ReadOptionalQuaternion(this IStateReader r)
        {
            bool has = r.ReadBool();
            return has ? r.ReadQuaternion() : null;
        }

        // Enums
        public static void WriteEnum<T>(this IStateWriter w, T value) where T : struct, Enum
            => w.WriteInt(Convert.ToInt32(value));

        public static T ReadEnum<T>(this IStateReader r) where T : struct, Enum
        {
            int raw = r.ReadInt();
            return (T)Enum.ToObject(typeof(T), raw);
        }

        // Lists / Arrays (primitives)
        public static void WriteIntList(this IStateWriter w, IList<int> list)
        {
            if (list == null) { w.WriteInt(-1); return; }
            w.WriteInt(list.Count);
            for (int i = 0; i < list.Count; i++) w.WriteInt(list[i]);
        }

        public static List<int> ReadIntList(this IStateReader r)
        {
            int count = r.ReadInt();
            if (count < 0) return null;
            var list = new List<int>(count);
            for (int i = 0; i < count; i++) list.Add(r.ReadInt());
            return list;
        }

        public static void WriteFloatList(this IStateWriter w, IList<float> list)
        {
            if (list == null) { w.WriteInt(-1); return; }
            w.WriteInt(list.Count);
            for (int i = 0; i < list.Count; i++) w.WriteFloat(list[i]);
        }

        public static List<float> ReadFloatList(this IStateReader r)
        {
            int count = r.ReadInt();
            if (count < 0) return null;
            var list = new List<float>(count);
            for (int i = 0; i < count; i++) list.Add(r.ReadFloat());
            return list;
        }

        public static void WriteBoolList(this IStateWriter w, IList<bool> list)
        {
            if (list == null) { w.WriteInt(-1); return; }
            w.WriteInt(list.Count);
            for (int i = 0; i < list.Count; i++) w.WriteBool(list[i]);
        }

        public static List<bool> ReadBoolList(this IStateReader r)
        {
            int count = r.ReadInt();
            if (count < 0) return null;
            var list = new List<bool>(count);
            for (int i = 0; i < count; i++) list.Add(r.ReadBool());
            return list;
        }

        public static void WriteStringList(this IStateWriter w, IList<string> list)
        {
            if (list == null) { w.WriteInt(-1); return; }
            w.WriteInt(list.Count);
            for (int i = 0; i < list.Count; i++) w.WriteString(list[i]);
        }

        public static List<string> ReadStringList(this IStateReader r)
        {
            int count = r.ReadInt();
            if (count < 0) return null;
            var list = new List<string>(count);
            for (int i = 0; i < count; i++) list.Add(r.ReadString());
            return list;
        }

        public static void WriteVector3List(this IStateWriter w, IList<Vector3> list)
        {
            if (list == null) { w.WriteInt(-1); return; }
            w.WriteInt(list.Count);
            for (int i = 0; i < list.Count; i++) w.WriteVector3(list[i]);
        }

        public static List<Vector3> ReadVector3List(this IStateReader r)
        {
            int count = r.ReadInt();
            if (count < 0) return null;
            var list = new List<Vector3>(count);
            for (int i = 0; i < count; i++) list.Add(r.ReadVector3());
            return list;
        }

        // Generic list helpers
        public static void WriteList<T>(this IStateWriter w, IList<T> list, Action<IStateWriter, T> writeItem)
        {
            if (writeItem == null) throw new ArgumentNullException(nameof(writeItem));

            if (list == null) { w.WriteInt(-1); return; }
            w.WriteInt(list.Count);
            for (int i = 0; i < list.Count; i++)
                writeItem(w, list[i]);
        }

        public static List<T> ReadList<T>(this IStateReader r, Func<IStateReader, T> readItem)
        {
            if (readItem == null) throw new ArgumentNullException(nameof(readItem));

            int count = r.ReadInt();
            if (count < 0) return null;

            var list = new List<T>(count);
            for (int i = 0; i < count; i++)
                list.Add(readItem(r));
            return list;
        }

        // Version helpers
        public static void WriteVersion(this IStateWriter w, int version)
            => w.WriteInt(version);

        public static int ReadVersion(this IStateReader r, int min = 1, int max = 1000)
        {
            int v = r.ReadInt();
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }
    }
}
