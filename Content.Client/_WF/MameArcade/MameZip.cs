using System.IO;
using System.IO.Compression;
using System.Text;

namespace Content.Client._WF.MameArcade;

/// <summary>
/// Reads the zips players import. The client sandbox has no access to ZipArchive's entry list, so this reads the
/// central directory itself and inflates entries with DeflateStream.
/// </summary>
public static class MameZip
{
    private const uint EndSignature = 0x06054b50;
    private const uint Zip64EndSignature = 0x06064b50;
    private const uint Zip64LocatorSignature = 0x07064b50;
    private const uint DirectorySignature = 0x02014b50;
    private const uint LocalSignature = 0x04034b50;
    private const int EndSize = 22;
    private const ushort Stored = 0;
    private const ushort Deflated = 8;

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>A file in a zip, with the CRC and size MAME identifies ROMs by.</summary>
    public readonly record struct Entry(string Name, uint Crc, long Size, long CompressedSize, ushort Method, long LocalHeader);

    /// <exception cref="InvalidDataException">The stream isn't a zip.</exception>
    public static List<Entry> ReadDirectory(Stream stream)
    {
        var tailLength = (int) Math.Min(stream.Length, 0xffff + EndSize);
        var tail = new byte[tailLength];
        stream.Position = stream.Length - tailLength;
        ReadFully(stream, tail);

        var end = -1;
        for (var i = tailLength - EndSize; i >= 0; i--)
        {
            if (U32(tail, i) == EndSignature)
            {
                end = i;
                break;
            }
        }

        if (end < 0)
            throw new InvalidDataException("No zip directory.");

        long count = U16(tail, end + 10);
        long size = U32(tail, end + 12);
        long offset = U32(tail, end + 16);

        if (end >= 20 && U32(tail, end - 20) == Zip64LocatorSignature)
        {
            var record = new byte[56];
            stream.Position = (long) U64(tail, end - 20 + 8);
            ReadFully(stream, record);
            if (U32(record, 0) != Zip64EndSignature)
                throw new InvalidDataException("Bad zip64 directory.");

            count = (long) U64(record, 32);
            size = (long) U64(record, 40);
            offset = (long) U64(record, 48);
        }

        if (size > int.MaxValue || offset + size > stream.Length)
            throw new InvalidDataException("Bad zip directory.");

        var directory = new byte[size];
        stream.Position = offset;
        ReadFully(stream, directory);

        var entries = new List<Entry>();
        var at = 0;
        for (var i = 0; i < count; i++)
        {
            if (at + 46 > directory.Length || U32(directory, at) != DirectorySignature)
                throw new InvalidDataException("Bad zip directory entry.");

            var method = U16(directory, at + 10);
            var crc = U32(directory, at + 16);
            long compressed = U32(directory, at + 20);
            long uncompressed = U32(directory, at + 24);
            var nameLength = U16(directory, at + 28);
            var extraLength = U16(directory, at + 30);
            var commentLength = U16(directory, at + 32);
            long local = U32(directory, at + 42);
            var name = Encoding.UTF8.GetString(directory, at + 46, nameLength);

            ReadZip64Sizes(directory, at + 46 + nameLength, extraLength, ref uncompressed, ref compressed, ref local);
            entries.Add(new Entry(name, crc, uncompressed, compressed, method, local));
            at += 46 + nameLength + extraLength + commentLength;
        }

        return entries;
    }

    /// <summary>Inflates an entry and checks it against its CRC.</summary>
    /// <exception cref="InvalidDataException">The data is damaged or uses an unsupported compression.</exception>
    public static byte[] Read(Stream stream, Entry entry)
    {
        var header = new byte[30];
        stream.Position = entry.LocalHeader;
        ReadFully(stream, header);
        if (U32(header, 0) != LocalSignature)
            throw new InvalidDataException($"Bad zip entry {entry.Name}.");

        stream.Position = entry.LocalHeader + 30 + U16(header, 26) + U16(header, 28);
        var data = new byte[entry.Size];

        switch (entry.Method)
        {
            case Stored:
                ReadFully(stream, data);
                break;
            case Deflated:
                using (var deflate = new DeflateStream(stream, CompressionMode.Decompress, true))
                {
                    ReadFully(deflate, data);
                }
                break;
            default:
                throw new InvalidDataException($"{entry.Name} uses an unsupported compression.");
        }

        if (Crc(data) != entry.Crc)
            throw new InvalidDataException($"{entry.Name} is damaged.");

        return data;
    }

    public static uint Crc(byte[] data)
    {
        var crc = 0xffffffffu;
        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static void ReadZip64Sizes(byte[] buffer, int start, int length, ref long uncompressed, ref long compressed, ref long local)
    {
        var at = start;
        while (at + 4 <= start + length)
        {
            var id = U16(buffer, at);
            var size = U16(buffer, at + 2);
            if (id == 0x0001)
            {
                var field = at + 4;
                if (uncompressed == 0xffffffff)
                {
                    uncompressed = (long) U64(buffer, field);
                    field += 8;
                }

                if (compressed == 0xffffffff)
                {
                    compressed = (long) U64(buffer, field);
                    field += 8;
                }

                if (local == 0xffffffff)
                    local = (long) U64(buffer, field);

                return;
            }

            at += 4 + size;
        }
    }

    private static void ReadFully(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer, read, buffer.Length - read);
            if (count == 0)
                throw new InvalidDataException("The zip ends early.");

            read += count;
        }
    }

    private static ushort U16(byte[] b, int i) => (ushort) (b[i] | (b[i + 1] << 8));

    private static uint U32(byte[] b, int i) => (uint) (b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));

    private static ulong U64(byte[] b, int i) => U32(b, i) | ((ulong) U32(b, i + 4) << 32);

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
