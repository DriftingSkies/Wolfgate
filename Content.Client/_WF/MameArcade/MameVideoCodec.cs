using System.IO;
using System.IO.Compression;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.MameArcade;

/// <summary>Which seats are played and what they hold, shown on a watcher's control deck.</summary>
public readonly record struct MameDeckState(MameControls PlayerOne, MameControls PlayerTwo, bool OneActive, bool TwoActive);

/// <summary>
/// Frames for spectators: 15-bit colour, only the 8x8 tiles that changed since the last packet, deflated. A keyframe
/// carries every tile, so a watcher can start from it. Raw pixels deflate better here than palettes, planes or deltas.
/// The header also carries both seats' controls for the watcher's control deck, and when the player's client took the
/// frame, so a watcher can show frames as evenly as they were taken.
/// </summary>
public static class MameVideoCodec
{
    public const int Tile = 8;
    public const byte KeyframeFlag = 1;
    public const byte OneActiveFlag = 2;
    public const byte TwoActiveFlag = 4;
    public const int HeaderBytes = 13;
    private const int StampAt = 9;

    // Larger than any Neo Geo screen, so a bad packet can't make a watcher allocate much.
    public const int MaxSide = 512;

    /// <summary>When the frame in a packet was taken, in milliseconds of the player's clock.</summary>
    public static bool TryReadStamp(byte[] packet, out uint stamp)
    {
        stamp = 0;
        if (packet.Length < HeaderBytes)
            return false;

        stamp = (uint) (packet[StampAt] | packet[StampAt + 1] << 8 | packet[StampAt + 2] << 16 | packet[StampAt + 3] << 24);
        return true;
    }

    public static int TileCount(int width, int height) => TilesAcross(width) * TilesAcross(height);

    public static int TilesAcross(int side) => (side + Tile - 1) / Tile;

    public static ushort Pack(Rgba32 pixel) => (ushort) ((pixel.R >> 3) << 10 | (pixel.G >> 3) << 5 | pixel.B >> 3);

    public static Rgba32 Unpack(ushort color)
    {
        var r = (color >> 10) & 0x1f;
        var g = (color >> 5) & 0x1f;
        var b = color & 0x1f;
        return new Rgba32((byte) (r << 3 | r >> 2), (byte) (g << 3 | g >> 2), (byte) (b << 3 | b >> 2), 255);
    }
}

/// <summary>Turns a player's frames into <see cref="MameVideoCodec"/> packets. Not thread-safe.</summary>
public sealed class MameVideoEncoder
{
    private const int Tile = MameVideoCodec.Tile;

    private ushort[] _previous = Array.Empty<ushort>();
    private ushort[] _current = Array.Empty<ushort>();
    private int _width;
    private int _height;

    // Kept between frames: at these sizes each one would otherwise land on the large object heap.
    private Rgba32[] _staging = Array.Empty<Rgba32>();
    private byte[] _mask = Array.Empty<byte>();
    private readonly MemoryStream _body = new();
    private readonly MemoryStream _packet = new();

    /// <summary>
    /// Copies a frame into a buffer this encoder owns, to hand to <see cref="Encode"/> on another thread. Reused, so
    /// don't stage another frame until that encode is done.
    /// </summary>
    public Rgba32[] Stage(Rgba32[] pixels)
    {
        if (_staging.Length != pixels.Length)
            _staging = new Rgba32[pixels.Length];

        Array.Copy(pixels, _staging, pixels.Length);
        return _staging;
    }

    /// <param name="stamp">When the frame was taken, in milliseconds of this client's clock.</param>
    public byte[] Encode(Rgba32[] pixels, int width, int height, bool keyframe, MameDeckState deck = default, uint stamp = 0)
    {
        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;
            _current = new ushort[width * height];
            _previous = new ushort[width * height];
            keyframe = true;
        }

        for (var i = 0; i < _current.Length; i++)
        {
            _current[i] = MameVideoCodec.Pack(pixels[i]);
        }

        var tilesX = MameVideoCodec.TilesAcross(width);
        var maskLength = (MameVideoCodec.TileCount(width, height) + 7) / 8;
        if (_mask.Length != maskLength)
            _mask = new byte[maskLength];
        else
            Array.Clear(_mask);

        var mask = _mask;
        var body = _body;
        body.SetLength(0);

        for (var ty = 0; ty < MameVideoCodec.TilesAcross(height); ty++)
        {
            for (var tx = 0; tx < tilesX; tx++)
            {
                if (!keyframe && !TileChanged(tx, ty))
                    continue;

                var tile = ty * tilesX + tx;
                mask[tile >> 3] |= (byte) (1 << (tile & 7));
                WriteTile(body, tx, ty);
            }
        }

        (_previous, _current) = (_current, _previous);

        var flags = keyframe ? MameVideoCodec.KeyframeFlag : (byte) 0;
        if (deck.OneActive)
            flags |= MameVideoCodec.OneActiveFlag;
        if (deck.TwoActive)
            flags |= MameVideoCodec.TwoActiveFlag;

        var packet = _packet;
        packet.SetLength(0);
        packet.WriteByte(flags);
        WriteUShort(packet, (ushort) width);
        WriteUShort(packet, (ushort) height);
        WriteUShort(packet, (ushort) deck.PlayerOne);
        WriteUShort(packet, (ushort) deck.PlayerTwo);
        WriteUShort(packet, (ushort) stamp);
        WriteUShort(packet, (ushort) (stamp >> 16));
        using (var deflate = new DeflateStream(packet, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(mask);
            body.Position = 0;
            body.CopyTo(deflate);
        }

        return packet.ToArray();
    }

    private bool TileChanged(int tx, int ty)
    {
        for (var y = ty * Tile; y < Math.Min(ty * Tile + Tile, _height); y++)
        {
            for (var x = tx * Tile; x < Math.Min(tx * Tile + Tile, _width); x++)
            {
                if (_current[y * _width + x] != _previous[y * _width + x])
                    return true;
            }
        }

        return false;
    }

    private void WriteTile(Stream body, int tx, int ty)
    {
        for (var y = ty * Tile; y < Math.Min(ty * Tile + Tile, _height); y++)
        {
            for (var x = tx * Tile; x < Math.Min(tx * Tile + Tile, _width); x++)
            {
                WriteUShort(body, _current[y * _width + x]);
            }
        }
    }

    private static void WriteUShort(Stream stream, ushort value)
    {
        stream.WriteByte((byte) value);
        stream.WriteByte((byte) (value >> 8));
    }
}

/// <summary>Rebuilds a spectated screen from <see cref="MameVideoCodec"/> packets. Waits for a keyframe to start.</summary>
public sealed class MameVideoDecoder
{
    private const int Tile = MameVideoCodec.Tile;

    private Rgba32[] _frame = Array.Empty<Rgba32>();

    // Reused between packets, as it's as big as a keyframe.
    private byte[] _body = Array.Empty<byte>();

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>The screen, valid after <see cref="Decode"/> returns true.</summary>
    public Rgba32[] Frame => _frame;

    /// <summary>The players' controls as of the last decoded packet.</summary>
    public MameDeckState Deck { get; private set; }

    /// <summary>False if the packet can't be applied: malformed, or a change before the first keyframe.</summary>
    public bool Decode(byte[] packet)
    {
        if (packet.Length < MameVideoCodec.HeaderBytes)
            return false;

        var keyframe = (packet[0] & MameVideoCodec.KeyframeFlag) != 0;
        var width = packet[1] | packet[2] << 8;
        var height = packet[3] | packet[4] << 8;
        if (width is 0 or > MameVideoCodec.MaxSide || height is 0 or > MameVideoCodec.MaxSide)
            return false;

        var deck = new MameDeckState(
            (MameControls) (packet[5] | packet[6] << 8),
            (MameControls) (packet[7] | packet[8] << 8),
            (packet[0] & MameVideoCodec.OneActiveFlag) != 0,
            (packet[0] & MameVideoCodec.TwoActiveFlag) != 0);

        if (width != Width || height != Height)
        {
            if (!keyframe)
                return false;

            Width = width;
            Height = height;
            _frame = new Rgba32[width * height];
        }

        var tiles = MameVideoCodec.TileCount(width, height);
        var maskBytes = (tiles + 7) / 8;

        // Never more than a keyframe holds.
        var bodyLength = maskBytes + width * height * 2;
        if (_body.Length != bodyLength)
            _body = new byte[bodyLength];

        var body = _body;
        int length;
        try
        {
            using var deflate = new DeflateStream(
                new MemoryStream(packet, MameVideoCodec.HeaderBytes, packet.Length - MameVideoCodec.HeaderBytes),
                CompressionMode.Decompress);
            length = Fill(deflate, body);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        if (length < maskBytes)
            return false;

        var tilesX = MameVideoCodec.TilesAcross(width);
        var at = maskBytes;
        for (var tile = 0; tile < tiles; tile++)
        {
            if ((body[tile >> 3] & (1 << (tile & 7))) == 0)
                continue;

            var tx = tile % tilesX;
            var ty = tile / tilesX;
            for (var y = ty * Tile; y < Math.Min(ty * Tile + Tile, height); y++)
            {
                for (var x = tx * Tile; x < Math.Min(tx * Tile + Tile, width); x++)
                {
                    if (at + 2 > length)
                        return false;

                    _frame[y * width + x] = MameVideoCodec.Unpack((ushort) (body[at] | body[at + 1] << 8));
                    at += 2;
                }
            }
        }

        Deck = deck;
        return true;
    }

    private static int Fill(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
                break;

            read += n;
        }

        return read;
    }
}
