#nullable disable
// Added for Wolfgate on 2026-11-03: the client sandbox forbids System.IO.BinaryWriter, which save states use.

using System.IO;

namespace Content.Client._WF.MameArcade.Engine
{
    /// <summary>Little-endian writer with the System.IO.BinaryWriter calls the emulator makes.</summary>
    public sealed class BinaryWriter
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer = new byte[8];

        public BinaryWriter(Stream stream)
        {
            _stream = stream;
        }

        public Stream BaseStream => _stream;

        public void Write(bool value) => _stream.WriteByte(value ? (byte)1 : (byte)0);
        public void Write(byte value) => _stream.WriteByte(value);
        public void Write(sbyte value) => _stream.WriteByte((byte)value);
        public void Write(short value) => WriteBytes((ulong)(ushort)value, 2);
        public void Write(ushort value) => WriteBytes(value, 2);
        public void Write(int value) => WriteBytes((uint)value, 4);
        public void Write(uint value) => WriteBytes(value, 4);
        public void Write(long value) => WriteBytes((ulong)value, 8);
        public void Write(ulong value) => WriteBytes(value, 8);
        public void Write(float value) => Write(System.BitConverter.SingleToInt32Bits(value));
        public void Write(double value) => Write(System.BitConverter.DoubleToInt64Bits(value));
        // BinaryReader.ReadChar expects UTF-8.
        public void Write(char value) => Write(System.Text.Encoding.UTF8.GetBytes(new[] { value }));
        public void Write(byte[] buffer) => _stream.Write(buffer, 0, buffer.Length);
        public void Write(byte[] buffer, int index, int count) => _stream.Write(buffer, index, count);

        public void Flush() => _stream.Flush();
        public void Close() => _stream.Flush();

        private void WriteBytes(ulong value, int count)
        {
            for (var i = 0; i < count; i++)
            {
                _buffer[i] = (byte)(value >> (i * 8));
            }

            _stream.Write(_buffer, 0, count);
        }
    }
}
