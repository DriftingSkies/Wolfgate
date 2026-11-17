using System.Numerics;
using Content.Shared._WG.Pool;
using Robust.Client.Graphics;

namespace Content.Client._WG.Pool;

/// <summary>
/// Draws a pool ball as a small shaded sphere, one square per pixel of it. The ball's markings are fixed to two
/// of its own axes, so turning those axes rolls the number spots and the stripe across its face.
/// </summary>
public static class PoolBallArt
{
    public static readonly Color Ivory = Color.FromHex("#F4F1E4");
    private static readonly Color CueDot = Color.FromHex("#C8372D");

    /// <summary>Colours of balls 1 to 8; a stripe wears the colour of the solid eight below it.</summary>
    private static readonly Color[] Colors =
    {
        Color.FromHex("#F0C419"),
        Color.FromHex("#2F6FE0"),
        Color.FromHex("#E0352B"),
        Color.FromHex("#7A3FC0"),
        Color.FromHex("#F07F1E"),
        Color.FromHex("#1E9E52"),
        Color.FromHex("#8E2B36"),
        Color.FromHex("#1A1A1C"),
    };

    private static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.5f, -0.6f, 0.62f));

    /// <summary>The spot axis facing the viewer and the stripe lying level: a ball at rest in a rack.</summary>
    public static readonly Vector3 FaceUp = Vector3.UnitZ;
    public static readonly Vector3 FaceSide = Vector3.UnitX;

    /// <summary>Turns a ball's axis for a roll of this many table units across the cloth.</summary>
    public static Vector3 Roll(Vector3 axis, Vector2 moved)
    {
        var distance = moved.Length();
        if (distance < 0.00001f)
            return axis;

        // The turn that carries the top of the ball the way it travels.
        var around = new Vector3(-moved.Y / distance, moved.X / distance, 0f);
        var angle = distance / PoolRules.BallRadius;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        return axis * cos + Vector3.Cross(around, axis) * sin + around * (Vector3.Dot(around, axis) * (1f - cos));
    }

    /// <summary>Draws a ball of this pixel radius. Spot and side are two of the ball's axes, at right angles.</summary>
    public static void Draw(DrawingHandleScreen handle, int ball, Vector2 centre, float radius, Vector3 spot, Vector3 side, float shade = 1f)
    {
        var band = Vector3.Cross(spot, side);
        var cells = Math.Clamp((int) MathF.Round(radius * 2f), 8, 20);
        var cell = radius * 2f / cells;
        var origin = centre - new Vector2(radius);
        var colour = ball == PoolRules.CueBall ? Ivory : Colors[(ball - 1) % 8];

        for (var y = 0; y < cells; y++)
        {
            var ny = (y + 0.5f) / cells * 2f - 1f;
            for (var x = 0; x < cells; x++)
            {
                var nx = (x + 0.5f) / cells * 2f - 1f;
                var flat = nx * nx + ny * ny;
                if (flat > 1f)
                    continue;

                var normal = new Vector3(nx, ny, MathF.Sqrt(1f - flat));
                var alongSpot = MathF.Abs(Vector3.Dot(normal, spot));
                var alongSide = MathF.Abs(Vector3.Dot(normal, side));
                var alongBand = MathF.Abs(Vector3.Dot(normal, band));

                Color pixel;
                if (ball == PoolRules.CueBall)
                    pixel = MathF.Max(alongSpot, MathF.Max(alongSide, alongBand)) > 0.95f ? CueDot : Ivory;
                else if (ball <= PoolRules.EightBall)
                    pixel = alongSpot > 0.8f ? Ivory : colour;
                else
                    pixel = alongBand > 0.55f || alongSpot > 0.88f ? Ivory : colour;

                // Three flat tones and a glint, lit from the top left, with the rim a step darker.
                var lit = Vector3.Dot(normal, Light);
                var tone = lit > 0.35f ? 1f : lit > -0.05f ? 0.82f : 0.64f;
                if (flat > 0.78f)
                    tone *= 0.72f;

                tone *= shade;
                pixel = lit > 0.93f
                    ? Color.InterpolateBetween(pixel, Color.White, 0.75f)
                    : new Color(pixel.R * tone, pixel.G * tone, pixel.B * tone);

                var at = origin + new Vector2(x, y) * cell;
                handle.DrawRect(new UIBox2(at, at + new Vector2(cell)), pixel);
            }
        }
    }
}
