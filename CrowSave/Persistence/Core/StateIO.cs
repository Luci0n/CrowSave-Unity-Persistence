using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace CrowSave.Persistence.Core
{
    public interface IStateWriter
    {
        void WriteBool(bool v);
        void WriteInt(int v);
        void WriteFloat(float v);
        void WriteString(string v);
        void WriteVector3(Vector3 v);
        void WriteQuaternion(Quaternion q);
        void WriteBytes(byte[] bytes);
    }

    public interface IStateReader
    {
        bool ReadBool();
        int ReadInt();
        float ReadFloat();
        string ReadString();
        Vector3 ReadVector3();
        Quaternion ReadQuaternion();
        byte[] ReadBytes();
    }

    public sealed class StateBinaryWriter : IStateWriter, IDisposable
    {
        private readonly MemoryStream _ms;
        private readonly BinaryWriter _bw;

        public StateBinaryWriter(int capacity = 256)
        {
            _ms = new MemoryStream(capacity);
            _bw = new BinaryWriter(_ms);
        }

        public byte[] ToArray() => _ms.ToArray();

        public void WriteBool(bool v) => _bw.Write(v);
        public void WriteInt(int v) => _bw.Write(v);
        public void WriteFloat(float v) => _bw.Write(v);
        public void WriteString(string v) => _bw.Write(v ?? "");
        public void WriteVector3(Vector3 v) { _bw.Write(v.x); _bw.Write(v.y); _bw.Write(v.z); }
        public void WriteQuaternion(Quaternion q) { _bw.Write(q.x); _bw.Write(q.y); _bw.Write(q.z); _bw.Write(q.w); }

        public void WriteBytes(byte[] bytes)
        {
            if (bytes == null) { _bw.Write(-1); return; }
            _bw.Write(bytes.Length);
            _bw.Write(bytes);
        }

        public void Dispose()
        {
            _bw?.Dispose();
            _ms?.Dispose();
        }
    }

    public sealed class StateBinaryReader : IStateReader, IDisposable
    {
        // These caps protect against corrupted/malicious state blobs.
        // Tune as needed, but keep them finite.
        private const int MaxByteArrayBytes = 64 * 1024 * 1024;   // 64 MB per byte[] field
        private const int MaxStringBytes = 1 * 1024 * 1024;       // 1 MB per string (UTF-8 bytes)

        private readonly MemoryStream _ms;
        private readonly BinaryReader _br;

        public StateBinaryReader(byte[] data)
        {
            _ms = new MemoryStream(data ?? Array.Empty<byte>());
            _br = new BinaryReader(_ms, Encoding.UTF8, leaveOpen: false);
        }

        public bool ReadBool() => _br.ReadBoolean();
        public int ReadInt() => _br.ReadInt32();
        public float ReadFloat() => _br.ReadSingle();

        public string ReadString()
        {
            // Use safe string read to prevent huge allocations.
            return ReadStringCapped(_br, MaxStringBytes, "state string");
        }

        public Vector3 ReadVector3()
        {
            var x = _br.ReadSingle(); var y = _br.ReadSingle(); var z = _br.ReadSingle();
            return new Vector3(x, y, z);
        }

        public Quaternion ReadQuaternion()
        {
            var x = _br.ReadSingle(); var y = _br.ReadSingle(); var z = _br.ReadSingle(); var w = _br.ReadSingle();
            return new Quaternion(x, y, z, w);
        }

        public byte[] ReadBytes()
        {
            int len = _br.ReadInt32();
            if (len < 0) return null;

            if (len > MaxByteArrayBytes)
                throw new InvalidDataException($"StateBinaryReader: byte[] length {len} exceeds cap {MaxByteArrayBytes}.");

            long remaining = _ms.Length - _ms.Position;
            if (len > remaining)
                throw new InvalidDataException($"StateBinaryReader: truncated byte[] (len {len} > remaining {remaining}).");

            var bytes = _br.ReadBytes(len);
            if (bytes.Length != len)
                throw new InvalidDataException($"StateBinaryReader: truncated byte[] (expected {len}, got {bytes.Length}).");

            return bytes;
        }

        public void Dispose()
        {
            _br?.Dispose();
            _ms?.Dispose();
        }

        // Safe string reader (BinaryWriter.Write(string) compatible)
        private static string ReadStringCapped(BinaryReader br, int maxBytes, string label)
        {
            int byteLen = Read7BitEncodedInt(br);

            if (byteLen < 0)
                throw new InvalidDataException($"StateBinaryReader: invalid {label} length {byteLen}.");

            if (byteLen > maxBytes)
                throw new InvalidDataException($"StateBinaryReader: {label} length {byteLen} exceeds cap {maxBytes}.");

            if (br.BaseStream.CanSeek)
            {
                long remaining = br.BaseStream.Length - br.BaseStream.Position;
                if (byteLen > remaining)
                    throw new InvalidDataException($"StateBinaryReader: truncated {label} (need {byteLen}, remaining {remaining}).");
            }

            if (byteLen == 0) return "";

            var bytes = br.ReadBytes(byteLen);
            if (bytes.Length != byteLen)
                throw new InvalidDataException($"StateBinaryReader: truncated {label} (expected {byteLen}, got {bytes.Length}).");

            return Encoding.UTF8.GetString(bytes);
        }

        private static int Read7BitEncodedInt(BinaryReader br)
        {
            // Same encoding scheme used internally by BinaryWriter for strings.
            int count = 0;
            int shift = 0;

            while (shift != 35)
            {
                int b = br.ReadByte();
                count |= (b & 0x7F) << shift;
                shift += 7;

                if ((b & 0x80) == 0)
                    return count;
            }

            throw new InvalidDataException("StateBinaryReader: invalid 7-bit encoded int (too many bytes).");
        }
    }
}
