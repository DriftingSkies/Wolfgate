using System.Numerics;
using Content.Shared._WF.Drydock;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Drydock;

/// <summary>
/// Draws the holographic parking frame and approach lights of every booked berth guide, in the station's local tiles.
/// </summary>
public sealed partial class WFDrydockGuideOverlay : Overlay
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private readonly SharedTransformSystem _transform;
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private readonly ShaderInstance _shader;

    private const float FrameThickness = 0.08f;
    private const float BracketThickness = 0.22f;
    private const float BracketLength = 1.5f;
    private const float ScanSweepPeriod = 2.5f;
    private const float ApproachSpacing = 1.5f;
    private const float PulsePeriod = 1.2f;
    private const float PulseReach = 1.8f;
    private const float LightRadius = 0.18f;
    private const float HaloRadius = 0.4f;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WFDrydockGuideOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entMan.System<SharedTransformSystem>();
        _shader = _proto.Index(UnshadedShader).Instance();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        handle.UseShader(_shader);

        var time = (float) _timing.RealTime.TotalSeconds;
        var query = _entMan.EntityQueryEnumerator<WFDrydockGuideComponent>();
        while (query.MoveNext(out _, out var guide))
        {
            if (guide.Station is not { } station
                || !_entMan.TryGetComponent<TransformComponent>(station, out var stationXform)
                || stationXform.MapID != args.MapId)
                continue;

            handle.SetTransform(_transform.GetWorldMatrix(station));
            DrawFrame(handle, guide, time);
            DrawApproach(handle, guide, time);
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    private void DrawFrame(DrawingHandleWorld handle, WFDrydockGuideComponent guide, float time)
    {
        var frame = Box2.CenteredAround(guide.Center, guide.Size);
        var left = frame.Left;
        var right = frame.Right;
        var bottom = frame.Bottom;
        var top = frame.Top;

        var pulse = MathF.Sin(time * MathF.Tau) * 0.04f;
        handle.DrawRect(frame, GuideColor(0.10f + pulse));

        // Scanlines every tile across the long axis, plus one sweeping line.
        var horizontal = guide.Size.X >= guide.Size.Y;
        var longMin = horizontal ? left : bottom;
        var longMax = horizontal ? right : top;
        var shortMin = horizontal ? bottom : left;
        var shortMax = horizontal ? top : right;
        var scanColor = GuideColor(0.07f);
        for (var along = longMin + 1f; along < longMax; along += 1f)
        {
            handle.DrawLine(Point(horizontal, along, shortMin), Point(horizontal, along, shortMax), scanColor);
        }

        var sweep = longMin + (time % ScanSweepPeriod) / ScanSweepPeriod * (longMax - longMin);
        handle.DrawRect(Bar(Point(horizontal, sweep - 0.1f, shortMin), Point(horizontal, sweep + 0.1f, shortMax)), GuideColor(0.35f));

        var outline = GuideColor(0.85f);
        var t = FrameThickness / 2f;
        handle.DrawRect(new Box2(left - t, top - t, right + t, top + t), outline);
        handle.DrawRect(new Box2(left - t, bottom - t, right + t, bottom + t), outline);
        handle.DrawRect(new Box2(left - t, bottom - t, left + t, top + t), outline);
        handle.DrawRect(new Box2(right - t, bottom - t, right + t, top + t), outline);

        var length = MathF.Min(BracketLength, MathF.Min(frame.Width, frame.Height) / 3f);
        DrawBracket(handle, left, top, 1f, -1f, length, outline);
        DrawBracket(handle, right, top, -1f, -1f, length, outline);
        DrawBracket(handle, left, bottom, 1f, 1f, length, outline);
        DrawBracket(handle, right, bottom, -1f, 1f, length, outline);
    }

    /// <summary>
    /// Draws an L-shaped corner bracket from a corner, extending along the inward directions dx and dy.
    /// </summary>
    private static void DrawBracket(DrawingHandleWorld handle, float x, float y, float dx, float dy, float length, Color color)
    {
        handle.DrawRect(Bar(x, y, x + dx * length, y + dy * BracketThickness), color);
        handle.DrawRect(Bar(x, y, x + dx * BracketThickness, y + dy * length), color);
    }

    private void DrawApproach(DrawingHandleWorld handle, WFDrydockGuideComponent guide, float time)
    {
        var delta = guide.PathEnd - guide.PathStart;
        var length = delta.Length();
        if (length < 0.01f)
            return;

        var unit = delta / length;
        var pulseDistance = time % PulsePeriod / PulsePeriod * length;
        var count = (int) (length / ApproachSpacing) + 1;
        for (var i = 0; i < count; i++)
        {
            var distance = i * ApproachSpacing;
            var position = guide.PathStart + unit * distance;

            // Idle at a quarter brightness, flashing to full as the chase pulse passes.
            var reach = MathF.Max(0f, 1f - MathF.Abs(distance - pulseDistance) / PulseReach);
            var brightness = 0.25f + 0.75f * reach;

            handle.DrawCircle(position, HaloRadius, GuideColor(0.12f * brightness));
            handle.DrawCircle(position, LightRadius, GuideColor(brightness));
        }
    }

    /// <summary>The hologram green at the given alpha.</summary>
    private static Color GuideColor(float alpha)
    {
        return new Color(0.25f, 1f, 0.5f, alpha);
    }

    /// <summary>
    /// Maps a position along the frame's long axis and across its short axis to a world point.
    /// </summary>
    private static Vector2 Point(bool horizontal, float along, float across)
    {
        return horizontal ? new Vector2(along, across) : new Vector2(across, along);
    }

    /// <summary>Box spanning two corner points, in any order.</summary>
    private static Box2 Bar(float x0, float y0, float x1, float y1)
    {
        return new Box2(MathF.Min(x0, x1), MathF.Min(y0, y1), MathF.Max(x0, x1), MathF.Max(y0, y1));
    }

    /// <summary>Box spanning two corner points, in any order.</summary>
    private static Box2 Bar(Vector2 a, Vector2 b)
    {
        return Bar(a.X, a.Y, b.X, b.Y);
    }
}
