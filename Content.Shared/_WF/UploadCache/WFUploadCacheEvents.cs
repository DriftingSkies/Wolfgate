using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using SpaceWizards.Sodium;

namespace Content.Shared._WF.UploadCache;

/// <summary>
/// Files the server shares at runtime (Symphony's lobby music and backgrounds), mounted under /Uploaded on both sides.
/// They used to ride the engine's uploaded-resources channel, which sends every file to every player on every join
/// and keeps nothing on disk; this channel lists them, and a client fetches only those it has not cached.
/// </summary>
public static class WFUploadCache
{
    /// <summary>Where the files appear to both sides' resource managers.</summary>
    public static readonly ResPath Root = new("/Uploaded");

    /// <summary>Transfer stream key the server sends requested files on.</summary>
    public const string TransferKey = "WFUploadCache";

    /// <summary>Largest file either side will take.</summary>
    public const int MaxFileBytes = 64 * 1024 * 1024;

    /// <summary>Lowercase hex Blake2b-256 of a file's bytes; its name in the client's disk cache.</summary>
    public static string Hash(ReadOnlySpan<byte> data)
    {
        var hash = CryptoGenericHashBlake2B.Hash(32, data, ReadOnlySpan<byte>.Empty);
        var chars = new char[hash.Length * 2];
        const string digits = "0123456789abcdef";
        for (var i = 0; i < hash.Length; i++)
        {
            chars[i * 2] = digits[hash[i] >> 4];
            chars[i * 2 + 1] = digits[hash[i] & 0xF];
        }

        return new string(chars);
    }

    /// <summary>
    /// Writes files to a transfer stream. Per file: hash length and UTF-8 hash, data length and data, lengths as
    /// little-endian int32. A zero hash length ends the stream.
    /// </summary>
    public static async Task WriteFiles(Stream stream, IEnumerable<(string Hash, byte[] Data)> files)
    {
        var lengths = new byte[4];
        foreach (var (hash, data) in files)
        {
            var name = Encoding.UTF8.GetBytes(hash);
            BinaryPrimitives.WriteInt32LittleEndian(lengths, name.Length);
            await stream.WriteAsync(lengths);
            await stream.WriteAsync(name);
            BinaryPrimitives.WriteInt32LittleEndian(lengths, data.Length);
            await stream.WriteAsync(lengths);
            await stream.WriteAsync(data);
        }

        BinaryPrimitives.WriteInt32LittleEndian(lengths, 0);
        await stream.WriteAsync(lengths);
    }

    /// <summary>Reads what <see cref="WriteFiles"/> wrote, handing each file over as it arrives.</summary>
    public static async Task ReadFiles(Stream stream, Action<string, byte[]> onFile)
    {
        var lengths = new byte[4];
        while (true)
        {
            await stream.ReadExactlyAsync(lengths);
            var nameLength = BinaryPrimitives.ReadInt32LittleEndian(lengths);
            if (nameLength == 0)
                return;

            if (nameLength is < 0 or > 128)
                throw new InvalidDataException($"bad hash length {nameLength}");

            var name = new byte[nameLength];
            await stream.ReadExactlyAsync(name);

            await stream.ReadExactlyAsync(lengths);
            var size = BinaryPrimitives.ReadInt32LittleEndian(lengths);
            if (size < 0 || size > MaxFileBytes)
                throw new InvalidDataException($"bad file size {size}");

            var data = new byte[size];
            await stream.ReadExactlyAsync(data);
            onFile(Encoding.UTF8.GetString(name), data);
        }
    }

    /// <summary>Whether a string is a hash this channel makes, so it is safe as a file name.</summary>
    public static bool IsHash(string value)
    {
        if (value.Length != 64)
            return false;

        foreach (var c in value)
        {
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }

        return true;
    }
}

/// <summary>One shared file: its path relative to <see cref="WFUploadCache.Root"/>, its hash and size.</summary>
[Serializable, NetSerializable]
public sealed class WFUploadEntry
{
    public string Path = string.Empty;
    public string Hash = string.Empty;
    public int Size;
}

/// <summary>Server to client: every file shared now. Sent on joining the lobby and whenever the set changes.</summary>
[Serializable, NetSerializable]
public sealed class WFUploadManifestEvent : EntityEventArgs
{
    public List<WFUploadEntry> Files = new();
}

/// <summary>Client to server: the hashes it has neither in memory nor on disk.</summary>
[Serializable, NetSerializable]
public sealed class WFUploadRequestEvent : EntityEventArgs
{
    public List<string> Hashes = new();
}

/// <summary>
/// Server to client: files sent on the game connection, when the transfer channel is not up for that player.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFUploadFilesEvent : EntityEventArgs
{
    public List<string> Hashes = new();
    public List<byte[]> Data = new();
}
