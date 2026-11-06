using System.Numerics;
using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// The Lattice made visible: a living beam of alien light between each pair of neighbouring primary anchors, leaving
/// each from the edge of its shield. A lost anchor breaks the two beams that ran to it.
/// </summary>
public sealed class WFLatticeBeamOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    /// <summary>How close an anchor must sit to its corner of the polygon to count as that corner's, in metres.</summary>
    private const float CornerMatch = 1200f;

    private const float GlowWidth = 7f;
    private const float BodyWidth = 2.4f;
    private const float StrandWidth = 0.35f;
    private const float StrandAmplitude = 1.1f;
    private const float StrandWavelength = 36f;
    private const float StrandSpeed = 2.5f;
    private const float StrandStep = 3f;
    private const float PulseSpacing = 220f;
    private const float PulseSpeed = 320f;
    private const float PulseRadius = 1.4f;

    private static readonly Color Glow = Color.FromHex("#b040e0");
    private static readonly Color Body = Color.FromHex("#d070ff");
    private static readonly Color StrandColor = Color.FromHex("#f4d8ff");

    private readonly List<Vector2?> _corners = new();
    private readonly List<Vector2> _verts = new();

    public WFLatticeBeamOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 5;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var map = args.MapUid;
        if (!_entities.TryGetComponent<WFLatticeFieldComponent>(map, out var field)
            || !_proto.TryIndex(field.Profile, out var profile)
            || profile.AnchorCount < 2)
            return;

        MatchCorners(field, profile);
        var view = args.WorldAABB.Enlarged(GlowWidth);
        var time = (float) _timing.RealTime.TotalSeconds;
        var handle = args.WorldHandle;
        for (var i = 0; i < _corners.Count; i++)
        {
            if (_corners[i] is not { } from || _corners[(i + 1) % _corners.Count] is not { } to)
                continue;

            var along = to - from;
            var length = along.Length();
            if (length <= profile.BeamOffset * 2f)
                continue;

            var dir = along / length;
            var start = from + dir * profile.BeamOffset;
            var end = to - dir * profile.BeamOffset;
            if (Clip(start, end, view) is not { } range)
                continue;

            var (t0, t1) = range;
            DrawBeam(handle, start, end, t0, t1, time, i);
        }
    }

    /// <summary>For each corner of the polygon, the live primary anchor standing at it, if there is one.</summary>
    private void MatchCorners(WFLatticeFieldComponent field, WFLatticeProfilePrototype profile)
    {
        _corners.Clear();
        foreach (var corner in WFLatticeMath.Corners(Vector2.Zero, profile.AnchorCount, profile.AnchorRadius, profile.AnchorAngle))
        {
            Vector2? found = null;
            var best = CornerMatch;
            for (var a = 0; a < field.Anchors.Count && a < field.Strengths.Count; a++)
            {
                // Relays hold less than a primary; only primaries carry the beams.
                if (field.Strengths[a] < 1f)
                    continue;

                var distance = Vector2.Distance(field.Anchors[a], corner);
                if (distance < best)
                {
                    best = distance;
                    found = field.Anchors[a];
                }
            }

            _corners.Add(found);
        }
    }

    private void DrawBeam(DrawingHandleWorld handle, Vector2 start, Vector2 end, float t0, float t1, float time, int seed)
    {
        var a = Vector2.Lerp(start, end, t0);
        var b = Vector2.Lerp(start, end, t1);
        var length = Vector2.Distance(start, end);
        var dir = (end - start) / length;
        var normal = new Vector2(-dir.Y, dir.X);
        var flicker = 0.85f + 0.15f * MathF.Sin(time * 7f + seed * 1.7f);

        Quad(handle, a, b, normal, GlowWidth, Glow.WithAlpha(0.10f * flicker));
        Quad(handle, a, b, normal, BodyWidth, Body.WithAlpha(0.32f * flicker));

        // Two strands twist round the beam's core, out of step with each other.
        var from = t0 * length;
        var to = t1 * length;
        Strand(handle, start, dir, normal, from, to, time, 0f);
        Strand(handle, start, dir, normal, from, to, time, MathF.PI);

        // Bright knots of light run the length of it.
        var offset = (time * PulseSpeed + seed * 97f) % PulseSpacing;
        for (var at = MathF.Ceiling((from - offset) / PulseSpacing) * PulseSpacing + offset; at <= to; at += PulseSpacing)
        {
            handle.DrawCircle(start + dir * at, PulseRadius * 2.2f, Body.WithAlpha(0.25f));
            handle.DrawCircle(start + dir * at, PulseRadius, StrandColor.WithAlpha(0.9f));
        }
    }

    private void Strand(DrawingHandleWorld handle, Vector2 start, Vector2 dir, Vector2 normal, float from, float to, float time, float phase)
    {
        _verts.Clear();
        for (var at = from; ; at += StrandStep)
        {
            var d = MathF.Min(at, to);
            var wave = MathF.Sin(d / StrandWavelength * MathF.Tau - time * StrandSpeed + phase) * StrandAmplitude;
            var centre = start + dir * d + normal * wave;
            _verts.Add(centre + normal * StrandWidth);
            _verts.Add(centre - normal * StrandWidth);
            if (d >= to)
                break;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _verts.ToArray(), StrandColor.WithAlpha(0.75f));
    }

    private static void Quad(DrawingHandleWorld handle, Vector2 a, Vector2 b, Vector2 normal, float width, Color color)
    {
        var half = normal * (width / 2f);
        Span<Vector2> quad = new[] { a + half, a - half, b + half, b - half };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, quad, color);
    }

    /// <summary>The share of a segment, 0 to 1 from start to end, that lies inside a box; null if none does.</summary>
    private static (float, float)? Clip(Vector2 start, Vector2 end, Box2 box)
    {
        var t0 = 0f;
        var t1 = 1f;
        var d = end - start;
        if (!Edge(-d.X, start.X - box.Left, ref t0, ref t1)
            || !Edge(d.X, box.Right - start.X, ref t0, ref t1)
            || !Edge(-d.Y, start.Y - box.Bottom, ref t0, ref t1)
            || !Edge(d.Y, box.Top - start.Y, ref t0, ref t1))
            return null;

        return t1 > t0 ? (t0, t1) : null;
    }

    private static bool Edge(float p, float q, ref float t0, ref float t1)
    {
        if (p == 0f)
            return q >= 0f;

        var r = q / p;
        if (p < 0f)
        {
            if (r > t1)
                return false;

            t0 = MathF.Max(t0, r);
        }
        else
        {
            if (r < t0)
                return false;

            t1 = MathF.Min(t1, r);
        }

        return true;
    }
}
