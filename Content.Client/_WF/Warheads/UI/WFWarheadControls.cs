using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.Warheads.UI;

/// <summary>Colours shared by the warhead panel's drawn controls.</summary>
public static class WFWarheadPalette
{
    public static readonly Color Green = Color.FromHex("#81FF14");
    public static readonly Color Amber = Color.FromHex("#FFB020");
    public static readonly Color Red = Color.FromHex("#FF3020");
    public static readonly Color Hazard = Color.FromHex("#F2C21B");
    public static readonly Color Panel = Color.FromHex("#14161A");
    public static readonly Color PanelEdge = Color.FromHex("#2C3038");
    public static readonly Color Off = Color.FromHex("#1C2018");
}

/// <summary>Black and yellow hazard stripes filling the control.</summary>
public sealed class WFHazardStripes : Control
{
    /// <summary>Scrolls the stripes sideways, for an armed warhead.</summary>
    public bool Animate;

    private float _offset;

    public WFHazardStripes()
    {
        MinHeight = 10;
        RectClipContent = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (Animate)
            _offset = (_offset + args.DeltaSeconds * 24f) % 40f;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var size = PixelSize;
        handle.DrawRect(new UIBox2(Vector2.Zero, size), Color.Black);

        var width = 10f * UIScale;
        var period = width * 2f;
        var h = size.Y;
        var verts = new List<Vector2>();
        for (var x = -h - period + _offset * UIScale % period; x < size.X + h; x += period)
        {
            var a = new Vector2(x, h);
            var b = new Vector2(x + width, h);
            var c = new Vector2(x + width + h, 0f);
            var d = new Vector2(x + h, 0f);
            verts.Add(a); verts.Add(b); verts.Add(c);
            verts.Add(a); verts.Add(c); verts.Add(d);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, verts.ToArray(), WFWarheadPalette.Hazard);
    }
}

/// <summary>The radiation trefoil, which spins and glows while the warhead counts down.</summary>
public sealed class WFRadiationSymbol : Control
{
    /// <summary>Degrees per second; zero holds it still.</summary>
    public float Spin;

    public Color Glow = Color.Transparent;

    private float _angle;
    private float _time;

    public WFRadiationSymbol()
    {
        MinSize = new Vector2(64, 64);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _angle = (_angle + Spin * args.DeltaSeconds) % 360f;
        _time += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var size = PixelSize;
        var centre = size / 2f;
        var r = MathF.Min(size.X, size.Y) / 2f - 2f;

        if (Glow.A > 0f)
        {
            var pulse = 0.6f + 0.4f * MathF.Sin(_time * 6f);
            for (var i = 3; i >= 1; i--)
            {
                handle.DrawCircle(centre, r + i * 2.5f * UIScale, Glow.WithAlpha(Glow.A * 0.18f * pulse));
            }
        }

        handle.DrawCircle(centre, r, WFWarheadPalette.Hazard);
        handle.DrawCircle(centre, r, Color.Black, false);

        // Three blades, 60 degrees wide, 120 apart, with a gap around the hub.
        var inner = r * 0.3f;
        var outer = r * 0.88f;
        var verts = new List<Vector2>();
        for (var blade = 0; blade < 3; blade++)
        {
            var start = MathHelper.DegreesToRadians(_angle + 90f + blade * 120f - 30f);
            const int steps = 12;
            var step = MathHelper.DegreesToRadians(60f) / steps;
            for (var i = 0; i < steps; i++)
            {
                var a0 = start + step * i;
                var a1 = a0 + step;
                var i0 = centre + new Vector2(MathF.Cos(a0), -MathF.Sin(a0)) * inner;
                var i1 = centre + new Vector2(MathF.Cos(a1), -MathF.Sin(a1)) * inner;
                var o0 = centre + new Vector2(MathF.Cos(a0), -MathF.Sin(a0)) * outer;
                var o1 = centre + new Vector2(MathF.Cos(a1), -MathF.Sin(a1)) * outer;
                verts.Add(i0); verts.Add(o0); verts.Add(o1);
                verts.Add(i0); verts.Add(o1); verts.Add(i1);
            }
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, verts.ToArray(), Color.Black);
        handle.DrawCircle(centre, r * 0.2f, Color.Black);
    }
}

/// <summary>A seven-segment readout of MM:SS, after an old bomb timer.</summary>
public sealed class WFSegmentDisplay : Control
{
    // Segments a-g lit for each digit, as bits 0-6.
    private static readonly byte[] Digits =
    {
        0b0111111, 0b0000110, 0b1011011, 0b1001111, 0b1100110,
        0b1101101, 0b1111101, 0b0000111, 0b1111111, 0b1101111,
    };

    public int Seconds;
    public Color Lit = WFWarheadPalette.Green;
    public bool BlinkColon;
    public bool Flash;

    private float _time;

    public WFSegmentDisplay()
    {
        MinSize = new Vector2(250, 84);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var size = PixelSize;
        handle.DrawRect(new UIBox2(Vector2.Zero, size), Color.FromHex("#050805"));
        handle.DrawRect(new UIBox2(Vector2.Zero, size), WFWarheadPalette.PanelEdge, false);

        var lit = Lit;
        if (Flash && (int) (_time * 4f) % 2 == 1)
            lit = lit.WithAlpha(0.35f);

        var dim = Color.InterpolateBetween(Color.FromHex("#050805"), Lit, 0.09f);

        var clamped = Math.Clamp(Seconds, 0, 99 * 60 + 59);
        var minutes = clamped / 60;
        var secs = clamped % 60;
        var chars = new[] { minutes / 10, minutes % 10, -1, secs / 10, secs % 10 };

        var pad = 8f * UIScale;
        var digitH = size.Y - pad * 2f;
        var digitW = digitH * 0.55f;
        var colonW = digitW * 0.45f;
        var gap = digitW * 0.25f;
        var total = digitW * 4f + colonW + gap * 4f;
        var x = (size.X - total) / 2f;

        foreach (var c in chars)
        {
            if (c < 0)
            {
                var colonOn = !BlinkColon || (int) (_time * 2f) % 2 == 0;
                var dot = digitW * 0.09f;
                var cx = x + colonW / 2f;
                handle.DrawCircle(new Vector2(cx, pad + digitH * 0.32f), dot, colonOn ? lit : dim);
                handle.DrawCircle(new Vector2(cx, pad + digitH * 0.68f), dot, colonOn ? lit : dim);
                x += colonW + gap;
                continue;
            }

            DrawDigit(handle, new Vector2(x, pad), digitW, digitH, Digits[c], lit, dim);
            x += digitW + gap;
        }
    }

    private static void DrawDigit(DrawingHandleScreen handle, Vector2 origin, float w, float h, byte mask, Color lit, Color dim)
    {
        var t = w * 0.16f;
        var half = h / 2f;

        // a, b, c, d, e, f, g as (start, end) along the digit's outline.
        var segs = new (Vector2 A, Vector2 B)[]
        {
            (new(0, 0), new(w, 0)),
            (new(w, 0), new(w, half)),
            (new(w, half), new(w, h)),
            (new(0, h), new(w, h)),
            (new(0, half), new(0, h)),
            (new(0, 0), new(0, half)),
            (new(0, half), new(w, half)),
        };

        for (var i = 0; i < 7; i++)
        {
            var on = (mask & (1 << i)) != 0;
            DrawSegment(handle, origin + segs[i].A, origin + segs[i].B, t, on ? lit : dim);
        }
    }

    /// <summary>A hexagonal bar between two points, pointed at both ends.</summary>
    private static void DrawSegment(DrawingHandleScreen handle, Vector2 a, Vector2 b, float t, Color color)
    {
        var dir = Vector2.Normalize(b - a);
        var normal = new Vector2(-dir.Y, dir.X) * (t / 2f);
        var inset = dir * (t * 0.7f);
        var p0 = a + inset * 0.4f;
        var p1 = a + inset + normal;
        var p2 = b - inset + normal;
        var p3 = b - inset * 0.4f;
        var p4 = b - inset - normal;
        var p5 = a + inset - normal;
        var verts = new[]
        {
            p0, p1, p5, p1, p2, p5, p5, p2, p4, p2, p3, p4,
        };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, verts, color);
    }
}

/// <summary>A round indicator lamp with a glow when lit.</summary>
public sealed class WFLamp : Control
{
    public Color Color = WFWarheadPalette.Green;
    public bool On;
    public bool Blink;

    private float _time;

    public WFLamp()
    {
        MinSize = new Vector2(22, 22);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var centre = PixelSize / 2f;
        var r = MathF.Min(PixelSize.X, PixelSize.Y) / 2f - 3f * UIScale;
        var lit = On && (!Blink || (int) (_time * 3f) % 2 == 0);

        if (lit)
            handle.DrawCircle(centre, r + 3f * UIScale, Color.WithAlpha(0.25f));

        handle.DrawCircle(centre, r, Color.FromHex("#0A0A0A"));
        handle.DrawCircle(centre, r * 0.8f, lit ? Color : Color.InterpolateBetween(Color.Black, Color, 0.15f));
        if (lit)
            handle.DrawCircle(centre - new Vector2(r * 0.25f, r * 0.25f), r * 0.22f, Color.White.WithAlpha(0.55f));
    }
}

/// <summary>A key cylinder: the slot stands upright when off and lies flat once the key is turned.</summary>
public sealed class WFKeyCylinder : Control
{
    public bool Turned;

    private float _angle;

    public WFKeyCylinder()
    {
        MinSize = new Vector2(48, 48);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var target = Turned ? 90f : 0f;
        _angle += (target - _angle) * Math.Min(args.DeltaSeconds * 10f, 1f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var centre = PixelSize / 2f;
        var r = MathF.Min(PixelSize.X, PixelSize.Y) / 2f - 2f;

        handle.DrawCircle(centre, r, Color.FromHex("#3A3F48"));
        handle.DrawCircle(centre, r * 0.82f, Color.FromHex("#9AA3AE"));
        handle.DrawCircle(centre, r * 0.82f, Color.FromHex("#5A616B"), false);

        var rad = MathHelper.DegreesToRadians(_angle);
        var dir = new Vector2(MathF.Sin(rad), -MathF.Cos(rad));
        var side = new Vector2(-dir.Y, dir.X);
        var len = r * 0.6f;
        var width = r * 0.14f;
        var a = centre - dir * len;
        var b = centre + dir * len;
        var slot = new[]
        {
            a - side * width, a + side * width, b + side * width,
            a - side * width, b + side * width, b - side * width,
        };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, slot, Color.FromHex("#1A1C20"));

        // Tick marks for OFF (top) and ARMED (right).
        handle.DrawCircle(centre + new Vector2(0, -r * 1.0f), 2f * UIScale, Turned ? Color.Gray : WFWarheadPalette.Green);
        handle.DrawCircle(centre + new Vector2(r * 1.0f, 0), 2f * UIScale, Turned ? WFWarheadPalette.Red : Color.Gray);
    }
}

/// <summary>A safety lever: down and green with the safety on, up and red with it off.</summary>
public sealed class WFSafetyLever : Control
{
    public bool SafetyOn = true;

    private float _pos = 1f;

    public WFSafetyLever()
    {
        MinSize = new Vector2(34, 64);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var target = SafetyOn ? 1f : 0f;
        _pos += (target - _pos) * Math.Min(args.DeltaSeconds * 10f, 1f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var size = PixelSize;
        var slotW = size.X * 0.28f;
        var slot = new UIBox2(size.X / 2f - slotW / 2f, size.Y * 0.1f, size.X / 2f + slotW / 2f, size.Y * 0.9f);
        handle.DrawRect(new UIBox2(Vector2.Zero, size), Color.FromHex("#202329"));
        handle.DrawRect(slot, Color.Black);

        var y = MathHelper.Lerp(size.Y * 0.2f, size.Y * 0.8f, _pos);
        var knob = new Vector2(size.X / 2f, y);
        var color = SafetyOn ? WFWarheadPalette.Green : WFWarheadPalette.Red;
        handle.DrawCircle(knob, size.X * 0.36f, Color.Black);
        handle.DrawCircle(knob, size.X * 0.3f, color);
        handle.DrawCircle(knob - new Vector2(size.X * 0.08f), size.X * 0.08f, Color.White.WithAlpha(0.5f));
    }
}
