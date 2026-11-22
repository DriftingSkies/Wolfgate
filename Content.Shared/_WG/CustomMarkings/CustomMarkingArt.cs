using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;
using static Content.Shared._WG.CustomMarkings.CustomMarkingRules;

namespace Content.Shared._WG.CustomMarkings;

/// <summary>
/// A custom marking as it is drawn. Its pixels: one frame, or several for an animated marking, each four facings on
/// a sheet laid out the way an RSI state is, south and north on the top row and east and west below. How long each
/// frame shows. Which pixels of the body under it are erased. And which of its own pixels glow.
/// </summary>
public sealed class CustomMarkingArt
{
    public const int South = 0;
    public const int North = 1;
    public const int East = 2;
    public const int West = 3;

    /// <summary>An art pixel at least this opaque hides what is under it.</summary>
    private const byte SolidAlpha = 128;

    /// <summary>
    /// Straight-alpha RGBA, one frame's sheet after another, each row by row. A fully transparent pixel is always
    /// all zero.
    /// </summary>
    public byte[] Pixels { get; private set; }

    /// <summary>How long each frame shows, in milliseconds. With one frame the marking is still and this means nothing.</summary>
    private int[] _frameTimes;

    /// <summary>
    /// The pixels of the body this marking hides, as a <see cref="CustomMarkingErase"/> mask. The same for every
    /// frame.
    /// </summary>
    public readonly byte[] Erase = new byte[EraseBytes];

    /// <summary>
    /// The pixels that glow, drawn unshaded so they stay bright in the dark: a <see cref="CustomMarkingErase"/> mask
    /// for each frame, one after another. Only a drawn pixel glows, and it keeps glowing when recoloured.
    /// </summary>
    // A frame's mask is sliced where it is used: the client sandbox rejects a content method returning a span.
    public byte[] Glow { get; private set; }

    /// <summary>
    /// Whether the animation plays back down to the first frame at the end instead of starting over. Only means
    /// anything with three frames or more; see <see cref="PingPongs"/>.
    /// </summary>
    public bool PingPong;

    public CustomMarkingArt()
    {
        Pixels = new byte[PixelBytes];
        _frameTimes = new[] { DefaultFrameTime };
        Glow = new byte[EraseBytes];
    }

    private CustomMarkingArt(byte[] pixels, int[] frameTimes, byte[]? glow = null)
    {
        Pixels = pixels;
        _frameTimes = frameTimes;
        Glow = glow ?? new byte[frameTimes.Length * EraseBytes];
    }

    public int Frames => _frameTimes.Length;

    /// <summary>Whether playing back makes a difference: with two frames it is the same as starting over.</summary>
    public bool PingPongs => PingPong && Frames > 2;

    /// <summary>
    /// Art from what a client uploads, or null when it isn't well formed: raw sheet bytes for a whole number of
    /// frames, a time for each frame, an erase mask of the right size and a glow mask for each frame. Null times
    /// and null masks are a still marking's and no mask. Everything is copied; glow on an undrawn pixel is dropped.
    /// </summary>
    /// <param name="maxFrames">Most frames to accept.</param>
    public static CustomMarkingArt? FromPixels(
        byte[]? rgba,
        int[]? frameTimes = null,
        byte[]? erase = null,
        int maxFrames = MaxFrames,
        bool pingPong = false,
        byte[]? glow = null)
    {
        if (rgba == null || rgba.Length == 0 || rgba.Length % PixelBytes != 0)
            return null;

        var frames = rgba.Length / PixelBytes;
        if (frames > Math.Min(maxFrames, MaxFrames)
            || frameTimes != null && frameTimes.Length != frames
            || erase != null && erase.Length != EraseBytes
            || glow != null && glow.Length != frames * EraseBytes)
            return null;

        var pixels = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0)
                continue;

            pixels[i] = rgba[i];
            pixels[i + 1] = rgba[i + 1];
            pixels[i + 2] = rgba[i + 2];
            pixels[i + 3] = rgba[i + 3];
        }

        var times = new int[frames];
        for (var i = 0; i < frames; i++)
        {
            times[i] = frameTimes == null ? DefaultFrameTime : Math.Clamp(frameTimes[i], MinFrameTime, MaxFrameTime);
        }

        var art = new CustomMarkingArt(pixels, times) { PingPong = pingPong && frames > 2 };
        erase?.CopyTo(art.Erase, 0);
        if (glow != null)
            art.SetGlowMask(glow);

        return art;
    }

    public CustomMarkingArt Clone()
    {
        var copy = new CustomMarkingArt(Pixels.AsSpan().ToArray(), _frameTimes.AsSpan().ToArray(), Glow.AsSpan().ToArray())
        {
            PingPong = PingPong,
        };
        Erase.CopyTo(copy.Erase, 0);
        return copy;
    }

    /// <summary>Becomes a copy of other art: its frames, their times, its erase mask and what glows.</summary>
    public void Restore(CustomMarkingArt other)
    {
        Pixels = other.Pixels.AsSpan().ToArray();
        _frameTimes = other._frameTimes.AsSpan().ToArray();
        Glow = other.Glow.AsSpan().ToArray();
        other.Erase.CopyTo(Erase, 0);
        PingPong = other.PingPong;
    }

    /// <summary>Whether nothing is drawn on any facing of any frame. The erase mask doesn't count.</summary>
    public bool IsBlank()
    {
        for (var i = 3; i < Pixels.Length; i += 4)
        {
            if (Pixels[i] != 0)
                return false;
        }

        return true;
    }

    /// <summary>Whether this is the same art: frames, times, erase mask and glow.</summary>
    public bool Same(CustomMarkingArt other)
    {
        return SamePixels(other)
               && _frameTimes.AsSpan().SequenceEqual(other._frameTimes)
               && PingPongs == other.PingPongs
               && Erase.AsSpan().SequenceEqual(other.Erase)
               && Glow.AsSpan().SequenceEqual(other.Glow);
    }

    public bool SamePixels(CustomMarkingArt other)
    {
        return Pixels.AsSpan().SequenceEqual(other.Pixels);
    }

    public static bool InFrame(int x, int y)
    {
        return x >= 0 && y >= 0 && x < FrameSize && y < FrameSize;
    }

    /// <summary>The nearest pixel value to a colour. Rounds, where the colour's own byte properties cut off.</summary>
    public static Rgba32 ToPixel(Color color)
    {
        return new Rgba32(Channel(color.R), Channel(color.G), Channel(color.B), Channel(color.A));

        static byte Channel(float value)
        {
            return (byte) Math.Clamp(MathF.Round(value * byte.MaxValue), byte.MinValue, byte.MaxValue);
        }
    }

    public static Color ToColor(Rgba32 pixel)
    {
        return new Color(pixel.R, pixel.G, pixel.B, pixel.A);
    }

    private static int Offset(int frame, int facing, int x, int y)
    {
        return frame * PixelBytes + ((facing / 2 * FrameSize + y) * SheetSize + facing % 2 * FrameSize + x) * 4;
    }

    public Rgba32 GetPixel(int frame, int facing, int x, int y)
    {
        var i = Offset(frame, facing, x, y);
        return new Rgba32(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    /// <summary>Sets one pixel. A pixel that is still drawn keeps its glow; one cleared loses it. Returns whether it changed.</summary>
    public bool SetPixel(int frame, int facing, int x, int y, Rgba32 pixel)
    {
        if (pixel.A == 0)
        {
            pixel = default;
            CustomMarkingErase.Set(Glow.AsSpan(frame * EraseBytes, EraseBytes), facing, x, y, false);
        }

        var i = Offset(frame, facing, x, y);
        if (Pixels[i] == pixel.R && Pixels[i + 1] == pixel.G && Pixels[i + 2] == pixel.B && Pixels[i + 3] == pixel.A)
            return false;

        Pixels[i] = pixel.R;
        Pixels[i + 1] = pixel.G;
        Pixels[i + 2] = pixel.B;
        Pixels[i + 3] = pixel.A;
        return true;
    }

    public void Clear(int frame, int facing)
    {
        for (var y = 0; y < FrameSize; y++)
        {
            Pixels.AsSpan(Offset(frame, facing, 0, y), FrameSize * 4).Clear();
        }

        CustomMarkingErase.Clear(Glow.AsSpan(frame * EraseBytes, EraseBytes), facing);
    }

    /// <summary>
    /// Redraws a facing from one as it was: each pixel takes the one <paramref name="from"/> names for it, glow
    /// and all, or nothing where that lies off the facing.
    /// </summary>
    private void Redraw(int frame, int source, int target, Func<int, int, (int X, int Y)> from)
    {
        var before = new Rgba32[FrameSize * FrameSize];
        var glowed = new bool[FrameSize * FrameSize];
        for (var y = 0; y < FrameSize; y++)
        {
            for (var x = 0; x < FrameSize; x++)
            {
                before[y * FrameSize + x] = GetPixel(frame, source, x, y);
                glowed[y * FrameSize + x] = IsGlowing(frame, source, x, y);
            }
        }

        for (var y = 0; y < FrameSize; y++)
        {
            for (var x = 0; x < FrameSize; x++)
            {
                var (fromX, fromY) = from(x, y);
                var inFrame = InFrame(fromX, fromY);
                SetPixel(frame, target, x, y, inFrame ? before[fromY * FrameSize + fromX] : default);
                CustomMarkingErase.Set(Glow.AsSpan(frame * EraseBytes, EraseBytes), target, x, y, inFrame && glowed[fromY * FrameSize + fromX]);
            }
        }
    }

    /// <summary>Copies one facing onto another, mirrored left to right when <paramref name="mirror"/> is set.</summary>
    public void CopyFacing(int frame, int from, int to, bool mirror)
    {
        Redraw(frame, from, to, (x, y) => (mirror ? FrameSize - 1 - x : x, y));
    }

    /// <summary>
    /// Flips a facing left to right about a vertical line, given as the sum of any two columns that swap places:
    /// 31 for the middle of the frame, 30 for column 15, where a body's middle is. Pixels flipped past an edge are
    /// lost.
    /// </summary>
    public void Flip(int frame, int facing, int axis)
    {
        Redraw(frame, facing, facing, (x, y) => (axis - x, y));
    }

    /// <summary>Moves a facing's pixels; those pushed past an edge are lost.</summary>
    public void Shift(int frame, int facing, int dx, int dy)
    {
        Redraw(frame, facing, facing, (x, y) => (x - dx, y - dy));
    }

    /// <summary>Recolours the pixel at a point and every same-coloured pixel joined to it by an edge.</summary>
    /// <param name="allowed">Where the fill may go, by x and y. Null lets it go anywhere on the facing.</param>
    public void Fill(int frame, int facing, int x, int y, Rgba32 pixel, Func<int, int, bool>? allowed = null)
    {
        if (!InFrame(x, y) || allowed != null && !allowed(x, y))
            return;

        var target = GetPixel(frame, facing, x, y);
        if (!SetPixel(frame, facing, x, y, pixel))
            return;

        var open = new Stack<(int X, int Y)>();
        open.Push((x, y));
        while (open.TryPop(out var at))
        {
            Spread(at.X + 1, at.Y);
            Spread(at.X - 1, at.Y);
            Spread(at.X, at.Y + 1);
            Spread(at.X, at.Y - 1);
        }

        void Spread(int nextX, int nextY)
        {
            if (InFrame(nextX, nextY)
                && (allowed == null || allowed(nextX, nextY))
                && GetPixel(frame, facing, nextX, nextY) == target
                && SetPixel(frame, facing, nextX, nextY, pixel))
                open.Push((nextX, nextY));
        }
    }

    /// <summary>
    /// Makes the drawn pixel at a point, and every pixel of the same colour joined to it by an edge, glow or stop
    /// glowing.
    /// </summary>
    public void FillGlow(int frame, int facing, int x, int y, bool glowing)
    {
        if (!InFrame(x, y) || GetPixel(frame, facing, x, y) is not { A: > 0 } target)
            return;

        var seen = new bool[FrameSize * FrameSize];
        seen[y * FrameSize + x] = true;
        var open = new Stack<(int X, int Y)>();
        open.Push((x, y));
        while (open.TryPop(out var at))
        {
            SetGlow(frame, facing, at.X, at.Y, glowing);
            Spread(at.X + 1, at.Y);
            Spread(at.X - 1, at.Y);
            Spread(at.X, at.Y + 1);
            Spread(at.X, at.Y - 1);
        }

        void Spread(int nextX, int nextY)
        {
            if (!InFrame(nextX, nextY) || seen[nextY * FrameSize + nextX] || GetPixel(frame, facing, nextX, nextY) != target)
                return;

            seen[nextY * FrameSize + nextX] = true;
            open.Push((nextX, nextY));
        }
    }

    /// <summary>
    /// A copy without the pixels a test picks out by facing and position, in every frame: those over a lost limb,
    /// say. The test is only asked about places where a frame draws something.
    /// </summary>
    public CustomMarkingArt Without(Func<int, int, int, bool> gone)
    {
        var copy = Clone();
        for (var facing = 0; facing < Facings; facing++)
        {
            for (var y = 0; y < FrameSize; y++)
            {
                for (var x = 0; x < FrameSize; x++)
                {
                    var drawn = false;
                    for (var frame = 0; frame < Frames && !drawn; frame++)
                    {
                        drawn = Pixels[Offset(frame, facing, x, y) + 3] != 0;
                    }

                    if (!drawn || !gone(facing, x, y))
                        continue;

                    for (var frame = 0; frame < Frames; frame++)
                    {
                        copy.SetPixel(frame, facing, x, y, default);
                    }
                }
            }
        }

        return copy;
    }

    /// <summary>How long a frame shows, in milliseconds.</summary>
    public int GetFrameTime(int frame)
    {
        return _frameTimes[frame];
    }

    /// <summary>Sets how long a frame shows, kept within what the rules allow. Returns whether it changed.</summary>
    public bool SetFrameTime(int frame, int milliseconds)
    {
        milliseconds = Math.Clamp(milliseconds, MinFrameTime, MaxFrameTime);
        if (_frameTimes[frame] == milliseconds)
            return false;

        _frameTimes[frame] = milliseconds;
        return true;
    }

    /// <summary>The time of each frame, in milliseconds, as a new array.</summary>
    public int[] GetFrameTimes()
    {
        return _frameTimes.AsSpan().ToArray();
    }

    /// <summary>
    /// Adds a frame after one, as a copy of it: an animation is mostly drawn by changing the frame before. Returns
    /// the new frame's index, or -1 when the art is already at <paramref name="maxFrames"/>.
    /// </summary>
    public int AddFrame(int after, int maxFrames = MaxFrames)
    {
        var frames = Frames;
        if (frames >= Math.Min(maxFrames, MaxFrames))
            return -1;

        var at = after + 1;
        var times = new int[frames + 1];
        for (var i = 0; i <= frames; i++)
        {
            times[i] = _frameTimes[i < at ? i : i - 1];
        }

        Pixels = CopyChunk(Pixels, PixelBytes, after);
        Glow = CopyChunk(Glow, EraseBytes, after);
        _frameTimes = times;
        return at;
    }

    /// <summary>Takes a frame out. Does nothing when it is the only one. Returns whether it was taken out.</summary>
    public bool RemoveFrame(int frame)
    {
        var frames = Frames;
        if (frames <= 1)
            return false;

        var times = new int[frames - 1];
        for (var i = 0; i < frames - 1; i++)
        {
            times[i] = _frameTimes[i < frame ? i : i + 1];
        }

        Pixels = RemoveChunk(Pixels, PixelBytes, frame);
        Glow = RemoveChunk(Glow, EraseBytes, frame);
        _frameTimes = times;
        return true;
    }

    /// <summary>Moves a frame to another place in the order, its time with it. Returns whether anything moved.</summary>
    public bool MoveFrame(int frame, int to)
    {
        var frames = Frames;
        if (frame == to || frame < 0 || to < 0 || frame >= frames || to >= frames)
            return false;

        var time = _frameTimes[frame];
        var step = to > frame ? 1 : -1;
        for (var i = frame; i != to; i += step)
        {
            _frameTimes[i] = _frameTimes[i + step];
        }

        _frameTimes[to] = time;
        MoveChunk(Pixels, PixelBytes, frame, to);
        MoveChunk(Glow, EraseBytes, frame, to);
        return true;
    }

    /// <summary>Puts the frames in the opposite order, each keeping its time. Returns whether anything moved.</summary>
    public bool ReverseFrames()
    {
        var frames = Frames;
        if (frames < 2)
            return false;

        for (var i = 0; i < frames / 2; i++)
        {
            var j = frames - 1 - i;
            SwapChunks(Pixels, PixelBytes, i, j);
            SwapChunks(Glow, EraseBytes, i, j);
            (_frameTimes[i], _frameTimes[j]) = (_frameTimes[j], _frameTimes[i]);
        }

        return true;
    }

    /// <summary>Per-frame data with a copy of one frame's chunk put in after it.</summary>
    private static byte[] CopyChunk(byte[] data, int size, int after)
    {
        var at = after + 1;
        var result = new byte[data.Length + size];
        data.AsSpan(0, at * size).CopyTo(result);
        data.AsSpan(after * size, size).CopyTo(result.AsSpan(at * size));
        data.AsSpan(at * size).CopyTo(result.AsSpan((at + 1) * size));
        return result;
    }

    /// <summary>Per-frame data without one frame's chunk.</summary>
    private static byte[] RemoveChunk(byte[] data, int size, int frame)
    {
        var result = new byte[data.Length - size];
        data.AsSpan(0, frame * size).CopyTo(result);
        data.AsSpan((frame + 1) * size).CopyTo(result.AsSpan(frame * size));
        return result;
    }

    /// <summary>Moves one frame's chunk of per-frame data to another place, shifting those between.</summary>
    private static void MoveChunk(byte[] data, int size, int frame, int to)
    {
        var moved = data.AsSpan(frame * size, size).ToArray();
        var step = to > frame ? 1 : -1;
        for (var i = frame; i != to; i += step)
        {
            data.AsSpan((i + step) * size, size).CopyTo(data.AsSpan(i * size));
        }

        moved.CopyTo(data.AsSpan(to * size));
    }

    private static void SwapChunks(byte[] data, int size, int i, int j)
    {
        var first = data.AsSpan(i * size, size).ToArray();
        data.AsSpan(j * size, size).CopyTo(data.AsSpan(i * size));
        first.CopyTo(data.AsSpan(j * size));
    }

    /// <summary>Gives every frame the same time, kept within the rules. Returns whether any changed.</summary>
    public bool SetAllFrameTimes(int milliseconds)
    {
        var changed = false;
        for (var i = 0; i < Frames; i++)
        {
            changed |= SetFrameTime(i, milliseconds);
        }

        return changed;
    }

    /// <summary>
    /// The order frames are shown in: each once, then, for art that <see cref="PingPongs"/>, back down through
    /// the middle ones to the first.
    /// </summary>
    public int[] PlayOrder()
    {
        var frames = Frames;
        var order = new int[PingPongs ? frames * 2 - 2 : frames];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i < frames ? i : frames * 2 - 2 - i;
        }

        return order;
    }

    /// <summary>
    /// The art with its frames laid out in <see cref="PlayOrder"/>, for a sprite to run through and start over,
    /// as an RSI only can. Itself when it doesn't play back.
    /// </summary>
    public CustomMarkingArt Played()
    {
        if (!PingPongs)
            return this;

        var order = PlayOrder();
        var pixels = new byte[order.Length * PixelBytes];
        var glow = new byte[order.Length * EraseBytes];
        var times = new int[order.Length];
        for (var i = 0; i < order.Length; i++)
        {
            Pixels.AsSpan(order[i] * PixelBytes, PixelBytes).CopyTo(pixels.AsSpan(i * PixelBytes));
            Glow.AsSpan(order[i] * EraseBytes, EraseBytes).CopyTo(glow.AsSpan(i * EraseBytes));
            times[i] = _frameTimes[order[i]];
        }

        var played = new CustomMarkingArt(pixels, times, glow);
        Erase.CopyTo(played.Erase, 0);
        return played;
    }

    /// <summary>Whether the body's pixel at a point is erased.</summary>
    public bool IsErased(int facing, int x, int y)
    {
        return CustomMarkingErase.Get(Erase, facing, x, y);
    }

    /// <summary>Erases the body's pixel at a point, or brings it back. Returns whether that changed anything.</summary>
    public bool SetErased(int facing, int x, int y, bool erased)
    {
        return CustomMarkingErase.Set(Erase, facing, x, y, erased);
    }

    public bool HasErase()
    {
        return CustomMarkingErase.Any(Erase);
    }

    /// <summary>Brings back every pixel of the body in one facing.</summary>
    public void ClearErase(int facing)
    {
        RedrawErase(facing, facing, (_, _) => (-1, -1));
    }

    /// <summary>As <see cref="CopyFacing"/>, for the erase mask.</summary>
    public void CopyErase(int from, int to, bool mirror)
    {
        RedrawErase(from, to, (x, y) => (mirror ? FrameSize - 1 - x : x, y));
    }

    /// <summary>As <see cref="Flip"/>, for the erase mask.</summary>
    public void FlipErase(int facing, int axis)
    {
        RedrawErase(facing, facing, (x, y) => (axis - x, y));
    }

    /// <summary>As <see cref="Shift"/>, for the erase mask.</summary>
    public void ShiftErase(int facing, int dx, int dy)
    {
        RedrawErase(facing, facing, (x, y) => (x - dx, y - dy));
    }

    private void RedrawErase(int source, int target, Func<int, int, (int X, int Y)> from)
    {
        var before = new bool[FrameSize * FrameSize];
        for (var y = 0; y < FrameSize; y++)
        {
            for (var x = 0; x < FrameSize; x++)
            {
                before[y * FrameSize + x] = IsErased(source, x, y);
            }
        }

        for (var y = 0; y < FrameSize; y++)
        {
            for (var x = 0; x < FrameSize; x++)
            {
                var (fromX, fromY) = from(x, y);
                SetErased(target, x, y, InFrame(fromX, fromY) && before[fromY * FrameSize + fromX]);
            }
        }
    }

    /// <summary>Whether the pixel at a point of a frame glows.</summary>
    public bool IsGlowing(int frame, int facing, int x, int y)
    {
        return CustomMarkingErase.Get(Glow.AsSpan(frame * EraseBytes, EraseBytes), facing, x, y);
    }

    /// <summary>
    /// Makes a drawn pixel glow, or stop glowing. An undrawn pixel can't glow. Returns whether that changed anything.
    /// </summary>
    public bool SetGlow(int frame, int facing, int x, int y, bool glowing)
    {
        if (glowing && Pixels[Offset(frame, facing, x, y) + 3] == 0)
            return false;

        return CustomMarkingErase.Set(Glow.AsSpan(frame * EraseBytes, EraseBytes), facing, x, y, glowing);
    }

    public bool HasGlow()
    {
        return CustomMarkingErase.Any(Glow);
    }

    /// <summary>Stops every pixel of one facing of a frame glowing.</summary>
    public void ClearGlow(int frame, int facing)
    {
        CustomMarkingErase.Clear(Glow.AsSpan(frame * EraseBytes, EraseBytes), facing);
    }

    /// <summary>
    /// Takes what glows from a mask for every frame, laid out as <see cref="Glow"/> is. Bits on undrawn pixels are
    /// dropped. False, changing nothing, when the mask doesn't fit the frames.
    /// </summary>
    public bool SetGlowMask(ReadOnlySpan<byte> glow)
    {
        if (glow.Length != Glow.Length)
            return false;

        Glow.AsSpan().Clear();
        for (var frame = 0; frame < Frames; frame++)
        {
            var mask = glow.Slice(frame * EraseBytes, EraseBytes);
            for (var facing = 0; facing < Facings; facing++)
            {
                for (var y = 0; y < FrameSize; y++)
                {
                    for (var x = 0; x < FrameSize; x++)
                    {
                        if (CustomMarkingErase.Get(mask, facing, x, y))
                            SetGlow(frame, facing, x, y, true);
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The art in two: the pixels that are lit like anything else, and those that glow. Both keep the frames, their
    /// times and how they play.
    /// </summary>
    public (CustomMarkingArt Lit, CustomMarkingArt Glowing) SplitGlow()
    {
        var lit = Clone();
        var glowing = Clone();
        for (var frame = 0; frame < Frames; frame++)
        {
            for (var facing = 0; facing < Facings; facing++)
            {
                for (var y = 0; y < FrameSize; y++)
                {
                    for (var x = 0; x < FrameSize; x++)
                    {
                        if (Pixels[Offset(frame, facing, x, y) + 3] == 0)
                            continue;

                        if (IsGlowing(frame, facing, x, y))
                            lit.SetPixel(frame, facing, x, y, default);
                        else
                            glowing.SetPixel(frame, facing, x, y, default);
                    }
                }
            }
        }

        return (lit, glowing);
    }

    /// <summary>
    /// The pixels this art hides what is under: those it draws solidly in every frame, as a
    /// <see cref="CustomMarkingErase"/> mask.
    /// </summary>
    public byte[] Solid()
    {
        var solid = new byte[EraseBytes];
        for (var facing = 0; facing < Facings; facing++)
        {
            for (var y = 0; y < FrameSize; y++)
            {
                for (var x = 0; x < FrameSize; x++)
                {
                    var always = true;
                    for (var frame = 0; frame < Frames && always; frame++)
                    {
                        always = Pixels[Offset(frame, facing, x, y) + 3] >= SolidAlpha;
                    }

                    if (always)
                        CustomMarkingErase.Set(solid, facing, x, y, true);
                }
            }
        }

        return solid;
    }

    /// <summary>
    /// A short name for exactly this art, for art the client derives from other art. Not a secure hash: the
    /// server's hash is what identifies art.
    /// </summary>
    public string Fingerprint()
    {
        // FNV-1a.
        var hash = 14695981039346656037UL;
        foreach (var value in Pixels)
        {
            hash = unchecked((hash ^ value) * 1099511628211UL);
        }

        foreach (var time in _frameTimes)
        {
            hash = unchecked((hash ^ (ulong) time) * 1099511628211UL);
        }

        foreach (var value in Erase)
        {
            hash = unchecked((hash ^ value) * 1099511628211UL);
        }

        if (PingPongs)
            hash = unchecked((hash ^ 1) * 1099511628211UL);

        if (HasGlow())
        {
            foreach (var value in Glow)
            {
                hash = unchecked((hash ^ value) * 1099511628211UL);
            }
        }

        return hash.ToString("x16");
    }

    /// <summary>
    /// The frames as one PNG sheet an RSI state can be loaded from: two facings to a row, all of the south
    /// facing's frames first, then the north's, the east's and the west's. A still marking's is its one sheet.
    /// </summary>
    public byte[] ToPng()
    {
        var frames = Frames;
        using var image = new Image<Rgba32>(SheetSize, SheetSize * frames);
        for (var frame = 0; frame < frames; frame++)
        {
            for (var facing = 0; facing < Facings; facing++)
            {
                var cell = facing * frames + frame;
                var left = cell % 2 * FrameSize;
                var top = cell / 2 * FrameSize;
                for (var y = 0; y < FrameSize; y++)
                {
                    for (var x = 0; x < FrameSize; x++)
                    {
                        image[left + x, top + y] = GetPixel(frame, facing, x, y);
                    }
                }
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Art from an image: a sheet as <see cref="ToPng"/> makes, or one facing, which becomes the south facing of a
    /// still marking. Null for any other size. <paramref name="pixels"/> holds the image row by row. Frame times
    /// and the erase mask aren't in an image; they are left at their defaults.
    /// </summary>
    public static CustomMarkingArt? FromImage(int width, int height, ReadOnlySpan<Rgba32> pixels)
    {
        if (pixels.Length != width * height)
            return null;

        if (width == FrameSize && height == FrameSize)
        {
            var single = new CustomMarkingArt();
            for (var y = 0; y < FrameSize; y++)
            {
                for (var x = 0; x < FrameSize; x++)
                {
                    single.SetPixel(0, South, x, y, pixels[y * FrameSize + x]);
                }
            }

            return single;
        }

        if (width != SheetSize || height == 0 || height % SheetSize != 0 || height / SheetSize > MaxFrames)
            return null;

        var frames = height / SheetSize;
        var times = new int[frames];
        for (var i = 0; i < frames; i++)
        {
            times[i] = DefaultFrameTime;
        }

        var art = new CustomMarkingArt(new byte[frames * PixelBytes], times);
        for (var frame = 0; frame < frames; frame++)
        {
            for (var facing = 0; facing < Facings; facing++)
            {
                var cell = facing * frames + frame;
                var left = cell % 2 * FrameSize;
                var top = cell / 2 * FrameSize;
                for (var y = 0; y < FrameSize; y++)
                {
                    for (var x = 0; x < FrameSize; x++)
                    {
                        art.SetPixel(frame, facing, x, y, pixels[(top + y) * width + left + x]);
                    }
                }
            }
        }

        return art;
    }
}
