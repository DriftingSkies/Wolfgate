using System.Numerics;
using Content.Client._WF.CombatConsole;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.Cockpit;

/// <summary>Shows drift direction against the fixed ship bow, alongside exact speed under glass.</summary>
public sealed class WFVelocityVectorInstrument : Control
{
    private readonly Func<WFCockpitVelocityReading?> _read;
    private readonly Font _labels;
    private readonly Font _digits;
    private readonly string _caption = Loc.GetString("wf-cockpit-velocity");
    private readonly string _forward = Loc.GetString("wf-cockpit-velocity-forward");
    private readonly string _aft = Loc.GetString("wf-cockpit-velocity-aft");
    private readonly string _port = Loc.GetString("wf-cockpit-velocity-port");
    private readonly string _starboard = Loc.GetString("wf-cockpit-velocity-starboard");
    private DrawVertexUV2DColor[] _vertices = Array.Empty<DrawVertexUV2DColor>();
    private WFCockpitVelocityReading? _reading;
    private WFGaugeReading _speed;
    private float _sampleElapsed;

    /// <summary>The longest the arrow gets, as a fraction of the dial face, leaving the compass labels clear.</summary>
    private const float ArrowReach = 0.5f;

    /// <summary>Exposes the live physical sample independently of drawing.</summary>
    public WFCockpitVelocityReading? Reading => _read();

    public WFVelocityVectorInstrument(Func<WFCockpitVelocityReading?> read)
    {
        _read = read;
        MinWidth = 88;
        SetHeight = 100;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Pass;
        ToolTip = Loc.GetString("wf-cockpit-velocity-help");
        var cache = IoCManager.Resolve<IResourceCache>();
        _labels = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 9);
        _digits = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 22);
        _reading = _read();
        Sample();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _reading = _read();
        _sampleElapsed += args.DeltaSeconds;
        if (_sampleElapsed < 0.1f)
            return;
        _sampleElapsed %= 0.1f;
        Sample();
    }

    private void Sample()
    {
        _speed = WFGaugeReading.Number(_reading?.Speed, 0, WFCockpitVelocityReading.FullScaleSpeed, "wf-gauge-unit-speed", 1);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var reading = _reading;
        var skin = WFInstrumentTheme.Skin;
        var digital = WFInstrumentTheme.Digital;
        var tint = reading == null ? skin.TextMuted : skin.Accent;
        var layout = WFGaugeScale.RoundDial(PixelWidth, PixelHeight, UIScale, true);
        var radius = layout.Radius;
        var center = layout.Center;
        var face = MathF.Max(1, radius - 7 * UIScale);
        if (digital)
        {
            WFConsoleDigital.Panel(handle, PixelSizeBox, UIScale, skin.Glass, skin.EdgeSoft);
            WFConsoleDigital.Arc(handle, center, radius, UIScale, 0, MathF.Tau, skin.EdgeLight, ref _vertices);
            WFConsoleDigital.Arc(handle, center, face, UIScale, 0, MathF.Tau, skin.AccentDim, ref _vertices);
        }
        else
        {
            handle.DrawCircle(center + new Vector2(0, 2 * UIScale), radius, Color.Black.WithAlpha(0.8f));
            handle.DrawCircle(center, radius, skin.EdgeLight);
            handle.DrawCircle(center, radius - 2 * UIScale, skin.EdgeSoft);
            handle.DrawCircle(center, radius - 4 * UIScale, skin.TextMuted);
            handle.DrawCircle(center, radius - 5 * UIScale, skin.Ink);
            handle.DrawCircle(center, face, skin.Glass);
        }
        for (var tick = 0; tick < 24; tick++)
        {
            var angle = tick * MathF.Tau / 24;
            var direction = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
            handle.DrawLine(center + direction * (face - (tick % 6 == 0 ? 5 : 2) * UIScale),
                center + direction * face, tick % 6 == 0 ? skin.Text : skin.EdgeLight);
        }
        var extent = face * 0.72f;
        Text(handle, _labels, _forward, center + new Vector2(0, -face * 0.86f), face * 0.9f, skin.Text);
        Text(handle, _labels, _aft, center + new Vector2(0, extent), face * 0.9f, skin.TextMuted);
        Text(handle, _labels, _port, center + new Vector2(-extent, 0), face * 0.35f, skin.TextMuted);
        Text(handle, _labels, _starboard, center + new Vector2(extent, 0), face * 0.35f, skin.TextMuted);
        // The speed has its own bar beneath the dial, so the arrow starts at the dial's centre.
        var origin = center;
        var bow = new[] { origin + new Vector2(0, -6 * UIScale), origin + new Vector2(-3, 4) * UIScale,
            origin + new Vector2(3, 4) * UIScale };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, bow, skin.TextMuted.WithAlpha(0.65f));
        if (reading?.ScreenDirection is { } motion)
        {
            var length = face * ArrowReach * reading.Value.SpeedFraction;
            Arrow(handle, origin, motion, length, MathF.Min(6 * UIScale, length * 0.5f), 0.9f * UIScale, tint);
            if (digital)
            {
                var angle = MathF.Atan2(motion.Y, motion.X);
                WFConsoleDigital.Arc(handle, center, radius - 3 * UIScale, 2 * UIScale,
                    angle - 0.12f, angle + 0.12f, tint, ref _vertices);
            }
        }
        else if (reading != null)
            WFConsoleDigital.Dot(handle, origin, 2 * UIScale, tint, ref _vertices);
        if (!digital)
            WFInstrumentGlass.Round(handle, center, face + 2 * UIScale, UIScale);

        var bar = layout.Readout;
        if (digital)
            WFConsoleDigital.Panel(handle, bar, UIScale, skin.Ink, skin.EdgeSoft, false, 4);
        else
        {
            handle.DrawRect(bar, skin.Ink);
            handle.DrawRect(bar, skin.EdgeSoft, false);
        }
        var inner = new UIBox2(bar.TopLeft + new Vector2(2 * UIScale, UIScale), bar.BottomRight - new Vector2(2 * UIScale, UIScale));
        if (_speed.Digits.Length > 0)
            WFInstrumentText.DrawReading(handle, _digits, _speed.Digits, _speed.Unit, inner, UIScale, tint, skin.TextMuted);
        else
            WFInstrumentText.DrawFitted(handle, _digits, _speed.Text, inner, UIScale, tint);

        var captionScale = WFInstrumentText.FitScale(handle, _labels, _caption, UIScale, PixelWidth - 16 * UIScale, 18 * UIScale);
        if (captionScale > 0)
        {
            var width = handle.GetDimensions(_labels, _caption, captionScale).X;
            handle.DrawString(_labels, new Vector2((PixelWidth - width) / 2,
                layout.CaptionBaseline - _labels.GetAscent(captionScale)), _caption, captionScale, skin.Text);
        }
    }

    /// <summary>Draws a weighty arrow so slow drift stays visible against the face.</summary>
    private static void Arrow(DrawingHandleScreen handle, Vector2 origin, Vector2 direction, float length, float head,
        float weight, Color color)
    {
        var side = new Vector2(-direction.Y, direction.X);
        var neck = origin + direction * MathF.Max(0, length - head);
        var half = side * weight;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
            new[] { origin - half, origin + half, neck + half, origin - half, neck + half, neck - half }, color);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
            new[] { origin + direction * length, neck + side * head * 0.6f, neck - side * head * 0.6f }, color);
    }

    private void Text(DrawingHandleScreen handle, Font font, string text, Vector2 center, float width, Color color)
    {
        var scale = WFInstrumentText.FitScale(handle, font, text, UIScale, width);
        if (scale == 0)
            return;
        var dimensions = handle.GetDimensions(font, text, scale);
        handle.DrawString(font, center - new Vector2(dimensions.X / 2, font.GetAscent(scale) / 2), text, scale, color);
    }
}
