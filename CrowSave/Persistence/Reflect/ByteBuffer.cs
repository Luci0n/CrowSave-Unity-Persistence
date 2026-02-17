using System;
using System.IO;
using System.Text;
using CrowSave.Persistence.Core;
using UnityEngine;

namespace CrowSave.Persistence.Reflect
{
    /// <summary>
    /// Expandable binary buffer implementing IStateWriter, used for per-field payloads.
    /// Payload-per-field makes the main blob forward-skippable.
    /// </summary>
    public sealed class ByteBuffer : IStateWriter, IDisposable
    {
        private byte[] _buf;
        private int _len;

        public int Length => _len;

        public ByteBuffer(int capacity = 256)
        {
            _buf = new byte[Mathf.Max(16, capacity)];
            _len = 0;
        }

        public void Reset() => _len = 0;

        public byte[] ToArray()
        {
            var arr = new byte[_len];
            Buffer.BlockCopy(_buf, 0, arr, 0, _len);
            return arr;
        }

        public void Dispose() { }

        private void Ensure(int add)
        {
            int need = _len + add;
            if (need <= _buf.Length) return;

            int cap = _buf.Length;
            while (cap < need) cap *= 2;

            var nb = new byte[cap];
            Buffer.BlockCopy(_buf, 0, nb, 0, _len);
            _buf = nb;
        }

        public void WriteBool(bool v)
        {
            Ensure(1);
            _buf[_len++] = (byte)(v ? 1 : 0);
        }

        public void WriteInt(int v)
        {
            Ensure(4);
            unchecked
            {
                _buf[_len++] = (byte)(v);
                _buf[_len++] = (byte)(v >> 8);
                _buf[_len++] = (byte)(v >> 16);
                _buf[_len++] = (byte)(v >> 24);
            }
        }

        public void WriteFloat(float v) => WriteInt(BitConverter.SingleToInt32Bits(v));

        public void WriteString(string s)
        {
            if (s == null) { WriteInt(-1); return; }
            var bytes = Encoding.UTF8.GetBytes(s);
            WriteInt(bytes.Length);
            WriteRawBytes(bytes);
        }

        public void WriteBytes(byte[] data)
        {
            if (data == null) { WriteInt(-1); return; }
            WriteInt(data.Length);
            WriteRawBytes(data);
        }

        public void WriteVector3(Vector3 v)
        {
            WriteFloat(v.x);
            WriteFloat(v.y);
            WriteFloat(v.z);
        }

        public void WriteQuaternion(Quaternion q)
        {
            WriteFloat(q.x);
            WriteFloat(q.y);
            WriteFloat(q.z);
            WriteFloat(q.w);
        }

        private void WriteRawBytes(byte[] data)
        {
            Ensure(data.Length);
            Buffer.BlockCopy(data, 0, _buf, _len, data.Length);
            _len += data.Length;
        }
    }

    /// <summary>
    /// Reader over byte[] implementing IStateReader (mutable position).
    /// </summary>
    public struct ByteBufferReader : IStateReader
    {
        private readonly byte[] _data;
        private int _pos;

        public int Position => _pos;

        public ByteBufferReader(byte[] data)
        {
            _data = data ?? Array.Empty<byte>();
            _pos = 0;
        }

        private void Need(int n)
        {
            if (_pos + n > _data.Length)
                throw new EndOfStreamException($"ByteBufferReader: need {_pos + n} bytes but only {_data.Length} available.");
        }

        public bool ReadBool()
        {
            Need(1);
            return _data[_pos++] != 0;
        }

        public int ReadInt()
        {
            Need(4);
            unchecked
            {
                int b0 = _data[_pos++];
                int b1 = _data[_pos++] << 8;
                int b2 = _data[_pos++] << 16;
                int b3 = _data[_pos++] << 24;
                return b0 | b1 | b2 | b3;
            }
        }

        public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt());

        public string ReadString()
        {
            int len = ReadInt();
            if (len < 0) return null;

            Need(len);
            string s = Encoding.UTF8.GetString(_data, _pos, len);
            _pos += len;
            return s;
        }

        public byte[] ReadBytes()
        {
            int len = ReadInt();
            if (len < 0) return null;

            Need(len);
            var arr = new byte[len];
            Buffer.BlockCopy(_data, _pos, arr, 0, len);
            _pos += len;
            return arr;
        }

        public Vector3 ReadVector3() => new Vector3(ReadFloat(), ReadFloat(), ReadFloat());
        public Quaternion ReadQuaternion() => new Quaternion(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());
    }
}
