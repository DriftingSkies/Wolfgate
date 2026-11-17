using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WG.ScratchTickets;

/// <summary>Where things sit on the printed scratch card, and the card stock painted under them.</summary>
public static class WGScratchCardArt
{
    public const float Width = 340f;
    public const float Height = 580f;

    /// <summary>Art pixels per card unit, so the print stays sharp when the UI is scaled up.</summary>
    public const int Scale = 2;

    public const int PixelWidth = (int) Width * Scale;
    public const int PixelHeight = (int) Height * Scale;

    public static readonly UIBox2 Close = new(306, 10, 328, 32);
    public static readonly UIBox2 Ribbon = new(34, 74, 306, 100);
    public static readonly UIBox2 Legend = new(14, 106, 326, 206);
    public static readonly UIBox2 Frame = new(44, 232, 296, 484);
    public static readonly UIBox2 Foil = new(50, 238, 290, 478);
    public static readonly UIBox2 Barcode = new(20, 510, 128, 542);

    private const float CornerRadius = 16f;
    private const float RibbonNotch = 10f;
    private const int Rays = 28;
    private static readonly Vector2 BurstCentre = new(Width / 2f, 42f);

    private static readonly Vector3 Cream = Rgb(0xF6EFD8);
    private static readonly Vector3 Gold = Rgb(0xF0BE3C);
    private static readonly Vector3 GoldDark = Rgb(0xA8741A);
    private static readonly Vector3 Paper = Rgb(0xF4EBD0);
    private static readonly Vector3 Brown = Rgb(0x6E4410);
    private static readonly Vector3 White = Rgb(0xFFFFFF);
    private static readonly Vector3 Black = Rgb(0x101010);

    /// <summary>The colours one kind of ticket is printed in.</summary>
    public readonly record struct Palette(Vector3 RayLight, Vector3 RayDark, Vector3 Glow, Vector3 Ribbon)
    {
        public Palette(Color rayLight, Color rayDark, Color glow, Color ribbon)
            : this(Rgb(rayLight), Rgb(rayDark), Rgb(glow), Rgb(ribbon))
        {
        }

        public Vector3 Halftone => RayDark * 0.6f;
    }

    /// <summary>Paints the card with nothing printed on it yet: stock, sunburst, ribbon, panels and barcode.</summary>
    public static Rgba32[] Paint(Palette palette)
    {
        var pixels = new Rgba32[PixelWidth * PixelHeight];

        for (var py = 0; py < PixelHeight; py++)
        {
            for (var px = 0; px < PixelWidth; px++)
            {
                var x = (px + 0.5f) / Scale;
                var y = (py + 0.5f) / Scale;

                var edge = RoundBox(x, y, new UIBox2(0, 0, Width, Height), CornerRadius);
                var alpha = Coverage(edge);
                if (alpha <= 0)
                    continue;

                var colour = Stock(palette, x, y, edge);
                Ribboned(ref colour, palette.Ribbon, x, y);
                Panels(ref colour, x, y);
                Barcoded(ref colour, x, y);

                pixels[py * PixelWidth + px] = new Rgba32(colour.X, colour.Y, colour.Z, alpha);
            }
        }

        return pixels;
    }

    /// <summary>The border, the sunburst behind the title and the halftone that darkens the foot of the card.</summary>
    private static Vector3 Stock(Palette palette, float x, float y, float edge)
    {
        if (edge > -3f)
            return Cream;

        if (edge > -7f)
            return edge < -6f ? GoldDark : Gold;

        var offset = new Vector2(x, y) - BurstCentre;
        var turn = (MathF.Atan2(offset.Y, offset.X) + MathF.PI) / MathF.Tau;
        var colour = (int) (turn * Rays) % 2 == 0 ? palette.RayLight : palette.RayDark;
        colour = Vector3.Lerp(colour, palette.Glow, MathF.Exp(-offset.Length() / 120f) * 0.6f);

        // Dots on a staggered grid, growing toward the bottom edge.
        const float pitch = 9f;
        var row = MathF.Floor(y / pitch);
        var shifted = x + (row % 2 == 0 ? 0f : pitch / 2f);
        var dot = new Vector2(shifted - (MathF.Floor(shifted / pitch) + 0.5f) * pitch, y - (row + 0.5f) * pitch);
        var radius = Math.Clamp((y - 200f) / 380f, 0f, 1f) * 4.2f;
        return Vector3.Lerp(colour, palette.Halftone, Coverage(dot.Length() - radius) * 0.55f);
    }

    private static void Ribboned(ref Vector3 colour, Vector3 ribbon, float x, float y)
    {
        if (y < Ribbon.Top || y > Ribbon.Bottom || x < Ribbon.Left || x > Ribbon.Right)
            return;

        // Swallowtail ends.
        var middle = (Ribbon.Top + Ribbon.Bottom) / 2f;
        var cut = RibbonNotch * (1f - MathF.Abs(y - middle) / (Ribbon.Height / 2f));
        if (x - Ribbon.Left < cut || Ribbon.Right - x < cut)
            return;

        colour = y < Ribbon.Top + 2f ? Vector3.Lerp(ribbon, White, 0.25f)
            : y > Ribbon.Bottom - 3f ? ribbon * 0.68f
            : ribbon;
    }

    private static void Panels(ref Vector3 colour, float x, float y)
    {
        var legend = RoundBox(x, y, Legend, 8f);
        colour = Vector3.Lerp(colour, Brown, Coverage(legend));
        colour = Vector3.Lerp(colour, Paper, Coverage(legend + 2f));

        var frame = RoundBox(x, y, Frame, 8f);
        colour = Vector3.Lerp(colour, Brown, Coverage(frame));
        colour = Vector3.Lerp(colour, Gold, Coverage(frame + 1.5f));
        colour = Vector3.Lerp(colour, GoldDark, Coverage(RoundBox(x, y, Foil, 0f) - 1f));
        colour = Vector3.Lerp(colour, Paper, Coverage(RoundBox(x, y, Foil, 0f)));
    }

    private static void Barcoded(ref Vector3 colour, float x, float y)
    {
        colour = Vector3.Lerp(colour, White, Coverage(RoundBox(x, y, Barcode, 3f)));

        if (x < Barcode.Left + 6f || x > Barcode.Right - 6f || y < Barcode.Top + 4f || y > Barcode.Bottom - 4f)
            return;

        var bar = (uint) ((x - Barcode.Left) / 1.5f);
        if (bar * 2654435761u >> 28 < 9)
            colour = Black;
    }

    /// <summary>Distance to a rounded box's edge, negative inside.</summary>
    private static float RoundBox(float x, float y, UIBox2 box, float radius)
    {
        var qx = MathF.Abs(x - (box.Left + box.Right) / 2f) - (box.Width / 2f - radius);
        var qy = MathF.Abs(y - (box.Top + box.Bottom) / 2f) - (box.Height / 2f - radius);
        var outside = new Vector2(MathF.Max(qx, 0f), MathF.Max(qy, 0f)).Length();
        return outside + MathF.Min(MathF.Max(qx, qy), 0f) - radius;
    }

    /// <summary>How much of a pixel lies inside a shape, from its distance to the edge.</summary>
    private static float Coverage(float distance)
    {
        return Math.Clamp(0.5f - distance * Scale, 0f, 1f);
    }

    private static Vector3 Rgb(Color colour)
    {
        return new Vector3(colour.R, colour.G, colour.B);
    }

    private static Vector3 Rgb(int hex)
    {
        return new Vector3((hex >> 16 & 0xFF) / 255f, (hex >> 8 & 0xFF) / 255f, (hex & 0xFF) / 255f);
    }
}
