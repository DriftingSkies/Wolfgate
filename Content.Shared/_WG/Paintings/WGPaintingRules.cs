using Robust.Shared.Utility;

namespace Content.Shared._WG.Paintings;

/// <summary>A canvas size, with where its art sits on the canvas and frame sprites.</summary>
/// <param name="ArtX">Left column of the art on the sprite sheet.</param>
/// <param name="ArtY">Top row of the art on the sprite sheet.</param>
public readonly record struct WGCanvasSize(int Width, int Height, bool Large, int ArtX, int ArtY)
{
    /// <summary>Side of the square sprite the canvas is drawn on.</summary>
    public int Sheet => Large ? 64 : 32;

    public ResPath Rsi => Large ? WGPaintingRules.LargeCanvasRsi : WGPaintingRules.CanvasRsi;

    /// <summary>The canvas sprite state; the wip splotch and frame overlay add <c>-wip</c> and <c>-frame</c>.</summary>
    public string State => $"{Width}x{Height}";

    /// <summary>The canvas entity of this size.</summary>
    public string Prototype => $"WGCanvas{Width}x{Height}";
}

/// <summary>Limits and layout shared by canvases, frames and the art app.</summary>
public static class WGPaintingRules
{
    public static readonly ResPath CanvasRsi = new("_WG/Paintings/canvas.rsi");
    public static readonly ResPath LargeCanvasRsi = new("_WG/Paintings/canvas_large.rsi");

    /// <summary>The canvas sizes, as tgstation has them.</summary>
    public static readonly WGCanvasSize[] Sizes =
    {
        new(11, 11, false, 10, 10),
        new(19, 19, false, 6, 6),
        new(23, 19, false, 4, 6),
        new(23, 23, false, 4, 4),
        new(24, 24, false, 4, 4),
        new(36, 24, true, 14, 20),
        new(45, 27, true, 9, 18),
    };

    /// <summary>A blank canvas's colour, which erasing paints back.</summary>
    public static readonly Color Blank = Color.White;

    public const int MaxTitleLength = 40;

    public const int HashLength = 64;

    /// <summary>Most pixels one paint message may change.</summary>
    public const int MaxStrokePixels = 1024;

    /// <summary>Colours a painter can save on a paint palette.</summary>
    public const int MaxSwatches = 24;

    /// <summary>Colours a paint palette remembers as used last.</summary>
    public const int MaxRecent = 8;

    /// <summary>Paintings on one page of the art app.</summary>
    public const int PageSize = 8;

    /// <summary>Most paintings one art request may name.</summary>
    public const int MaxRequestedArt = 16;

    public static WGCanvasSize? GetSize(int width, int height)
    {
        foreach (var size in Sizes)
        {
            if (size.Width == width && size.Height == height)
                return size;
        }

        return null;
    }

    public static bool IsValidHash(string? hash)
    {
        if (hash is not { Length: HashLength })
            return false;

        foreach (var c in hash)
        {
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    /// <summary>A blank canvas of the given size, as RGBA bytes.</summary>
    public static byte[] BlankPixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
            Write(pixels, i, Blank);
        return pixels;
    }

    /// <summary>The colour as a canvas stores it: opaque, one byte per channel.</summary>
    public static Color Quantize(Color colour)
    {
        var bytes = new byte[4];
        Write(bytes, 0, colour);
        return Read(bytes, 0);
    }

    public static Color Read(byte[] pixels, int offset)
    {
        return new Color(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    public static void Write(byte[] pixels, int offset, Color colour)
    {
        pixels[offset] = (byte) Math.Round(colour.R * 255);
        pixels[offset + 1] = (byte) Math.Round(colour.G * 255);
        pixels[offset + 2] = (byte) Math.Round(colour.B * 255);
        pixels[offset + 3] = 255;
    }
}
