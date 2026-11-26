using System.Numerics;
using Content.Shared._WG.Pyramid;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.Pyramid;

/// <summary>The Pyramid's waves running out across space, bending and splitting the world inside each front.</summary>
public sealed partial class WGPyramidWaveOverlay : Overlay
{
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "WGPyramidWave";
    private readonly WGPyramidSystem _system;
    private readonly ShaderInstance _shader;
    private readonly Vector2[] _centres = new Vector2[4];
    private Vector4 _radii;
    private Vector4 _strengths;
    private float _reach;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public override bool RequestScreenTexture => true;

    public WGPyramidWaveOverlay(WGPyramidSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        ZIndex = 191;
        _shader = _proto.Index(Shader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var count = 0;
        var radii = new float[4];
        var strengths = new float[4];
        _reach = 0f;
        var pad = WGPyramidMath.WaveWidth * 3f;
        foreach (var wave in _system.Waves)
        {
            if (count == 4)
                break;

            if (wave.Event.Map != args.MapId)
                continue;

            var front = _system.Front(wave);
            var near = Vector2.Distance(wave.Event.Origin, args.WorldAABB.ClosestPoint(wave.Event.Origin));
            var far = FarthestCorner(args.WorldAABB, wave.Event.Origin);
            if (front - pad > far || front + pad < near)
                continue;

            _centres[count] = wave.Event.Origin;
            radii[count] = front;
            strengths[count] = wave.Event.Strength;
            _reach = MathF.Max(_reach, wave.Event.Reach);
            count++;
        }

        for (var i = count; i < 4; i++)
        {
            _centres[i] = Vector2.Zero;
            radii[i] = 0f;
            strengths[i] = 0f;
        }

        _radii = new Vector4(radii[0], radii[1], radii[2], radii[3]);
        _strengths = new Vector4(strengths[0], strengths[1], strengths[2], strengths[3]);
        return count > 0;
    }

    private static float FarthestCorner(Box2 box, Vector2 from)
    {
        return MathF.Max(MathF.Max(Vector2.Distance(box.BottomLeft, from), Vector2.Distance(box.BottomRight, from)),
            MathF.Max(Vector2.Distance(box.TopLeft, from), Vector2.Distance(box.TopRight, from)));
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var origin = args.Viewport.WorldToLocal(Vector2.Zero);
        var right = args.Viewport.WorldToLocal(Vector2.UnitX) - origin;
        var up = args.Viewport.WorldToLocal(Vector2.UnitY) - origin;
        right.Y = -right.Y;
        up.Y = -up.Y;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("viewportMin", args.WorldAABB.BottomLeft);
        _shader.SetParameter("viewportSize", args.WorldAABB.Size);
        _shader.SetParameter("worldToScreenX", right);
        _shader.SetParameter("worldToScreenY", up);
        _shader.SetParameter("centre0", _centres[0]);
        _shader.SetParameter("centre1", _centres[1]);
        _shader.SetParameter("centre2", _centres[2]);
        _shader.SetParameter("centre3", _centres[3]);
        _shader.SetParameter("radii", _radii);
        _shader.SetParameter("strengths", _strengths);
        _shader.SetParameter("width", WGPyramidMath.WaveWidth);
        _shader.SetParameter("reach", _reach);

        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldAABB, Color.White);
        args.WorldHandle.UseShader(null);
    }
}

/// <summary>The moment a wave passes you: rows and columns slide apart and the colours split, then settle.</summary>
public sealed partial class WGPyramidWarpOverlay : Overlay
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "WGPyramidWarp";
    private readonly ShaderInstance _shader;
    private TimeSpan _start;
    private float _peak;
    private float _strength;

    /// <summary>True with reduced motion: only the colours split.</summary>
    public bool Still;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public override bool RequestScreenTexture => true;

    public WGPyramidWarpOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 192;
        _shader = _proto.Index(Shader).InstanceUnique();
    }

    public void Start(TimeSpan now, float strength)
    {
        _start = now;
        _peak = MathF.Max(strength, _strength);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var t = (float) (_timing.CurTime - _start).TotalSeconds / WGPyramidMath.WarpSeconds;
        // Snaps in, then eases out.
        _strength = t is < 0f or > 1f ? 0f : _peak * MathF.Min(1f, t * 8f) * (1f - t) * (1f - t);
        return _strength > 0.001f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("strength", _strength);
        _shader.SetParameter("still", Still);
        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldAABB, Color.White);
        args.WorldHandle.UseShader(null);
    }
}

/// <summary>Shared helpers for the Pyramid's overlays.</summary>
internal static class WGPyramidDraw
{
    /// <summary>The eye's offset from the Pyramid's centre in screen-space metres; lasers and tethers leave from it.</summary>
    public static readonly Vector2 Eye = new(0.5f, 4f);

    public static Vector2 FromScreen(Vector2 screen, Vector2 up)
    {
        return new Vector2(up.Y, -up.X) * screen.X + up * screen.Y;
    }

    /// <summary>The world-to-screen axes the distortion shaders need, in pixels per metre.</summary>
    public static (Vector2 Right, Vector2 Up) Axes(in OverlayDrawArgs args)
    {
        var origin = args.Viewport.WorldToLocal(Vector2.Zero);
        var right = args.Viewport.WorldToLocal(Vector2.UnitX) - origin;
        var up = args.Viewport.WorldToLocal(Vector2.UnitY) - origin;
        right.Y = -right.Y;
        up.Y = -up.Y;
        return (right, up);
    }

    public static void Line(DrawingHandleWorld handle, Vector2 from, Vector2 to, float width, Color colour)
    {
        var delta = to - from;
        var length = delta.Length();
        if (length < 0.01f)
            return;

        var middle = (from + to) / 2f;
        var box = Box2.CenteredAround(middle, new Vector2(length, width));
        handle.DrawRect(new Box2Rotated(box, new Angle(MathF.Atan2(delta.Y, delta.X)), middle), colour);
    }

    /// <summary>A crackling line: straight segments kinked sideways at random, ends pinned.</summary>
    public static void Bolt(DrawingHandleWorld handle, Random random, Vector2 from, Vector2 to, float jitter, float width,
        Color colour)
    {
        var delta = to - from;
        var length = delta.Length();
        if (length < 0.01f)
            return;

        var side = new Vector2(-delta.Y, delta.X) / length;
        var steps = Math.Clamp((int) (length / 5f), 6, 48);
        var last = from;
        for (var i = 1; i <= steps; i++)
        {
            var point = from + delta * (i / (float) steps);
            if (i < steps)
                point += side * ((float) random.NextDouble() - 0.5f) * 2f * jitter;

            Line(handle, last, point, width, colour);
            last = point;
        }
    }
}

/// <summary>
/// The Pyramid's lasers and their charge. While a barrage charges, crackling tethers reach from its eye to every mark,
/// faint and wild at first, straightening and brightening as the charge sound builds, flickering in its last second;
/// then the lasers fire as bolts with a pale core in a red sheath.
/// </summary>
public sealed partial class WGPyramidBeamOverlay : Overlay
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEntityManager _entities = default!;

    private static readonly Color Core = Color.FromHex("#e6f6ff");
    private static readonly Color Glow = Color.FromHex("#9fd3ff");
    private static readonly Color Sheath = Color.FromHex("#e8482a");
    private readonly WGPyramidSystem _system;
    private readonly Random _random = new();
    private readonly List<(Vector2 From, Vector2 To, float Charge)> _tethers = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WGPyramidBeamOverlay(WGPyramidSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        ZIndex = 186;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _tethers.Clear();
        var transforms = _entities.System<SharedTransformSystem>();
        var up = _system.ScreenUp;
        var marks = _entities.EntityQueryEnumerator<WGPyramidMarkComponent, TransformComponent>();
        while (marks.MoveNext(out var mark, out _, out var markXform))
        {
            if (markXform.MapID != args.MapId)
                continue;

            var to = transforms.GetWorldPosition(markXform);
            Vector2? best = null;
            var bestDistance = 700f;
            var pyramids = _entities.EntityQueryEnumerator<WGPyramidVisualsComponent, TransformComponent>();
            while (pyramids.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapID != args.MapId)
                    continue;

                var at = transforms.GetWorldPosition(xform);
                var distance = Vector2.Distance(at, to);
                if (distance < bestDistance)
                {
                    best = at;
                    bestDistance = distance;
                }
            }

            if (best is { } from)
                _tethers.Add((from + WGPyramidDraw.FromScreen(WGPyramidDraw.Eye, up), to, _system.Charge(mark)));
        }

        if (_tethers.Count > 0)
            return true;

        foreach (var (beam, _) in _system.Beams)
        {
            if (beam.Map == args.MapId)
                return true;
        }

        return false;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        foreach (var (from, to, charge) in _tethers)
        {
            var flicker = charge > 0.83f && _random.NextDouble() < 0.5 ? 0.35f : 1f;
            var jitter = (1f - charge) * 2.2f + 0.15f;
            WGPyramidDraw.Bolt(handle, _random, from, to, jitter, 0.5f + 0.4f * charge, Glow.WithAlpha((0.08f + 0.25f * charge) * flicker));
            WGPyramidDraw.Bolt(handle, _random, from, to, jitter * 0.6f, 0.1f + 0.15f * charge, Core.WithAlpha((0.15f + 0.6f * charge) * flicker));
        }

        var now = _timing.CurTime;
        var up = _system.ScreenUp;
        foreach (var (beam, fired) in _system.Beams)
        {
            if (beam.Map != args.MapId)
                continue;

            var fade = 1f - (float) (now - fired).TotalSeconds / WGPyramidMath.BeamSeconds;
            if (fade <= 0f)
                continue;

            var from = beam.From + WGPyramidDraw.FromScreen(WGPyramidDraw.Eye, up);
            foreach (var to in beam.To)
            {
                WGPyramidDraw.Line(handle, from, to, 3.2f * fade, Sheath.WithAlpha(0.3f * fade));
                WGPyramidDraw.Bolt(handle, _random, from, to, 0.9f * fade, 1.2f, Glow.WithAlpha(0.75f * fade));
                WGPyramidDraw.Bolt(handle, _random, from, to, 0.35f * fade, 0.35f, Core.WithAlpha(fade));
            }
        }
    }
}

/// <summary>The fragments of chrome shed by the Pyramid: small tumbling flakes that glint and fade as they rise.</summary>
public sealed partial class WGPyramidFragmentOverlay : Overlay
{
    private readonly WGPyramidSystem _system;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WGPyramidFragmentOverlay(WGPyramidSystem system)
    {
        _system = system;
        ZIndex = 185;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _system.Fragments.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var view = args.WorldAABB.Enlarged(2f);
        foreach (var f in _system.Fragments)
        {
            if (f.Map != args.MapId || !view.Contains(f.Position))
                continue;

            var u = f.Age / f.Life;
            var fadeIn = MathF.Min(1f, f.Age / 0.12f);
            // A flake glints each time it turns its face to you.
            var glint = 0.55f + 0.45f * MathF.Abs(MathF.Sin(f.Angle));
            var alpha = MathF.Pow(1f - u, 0.8f) * fadeIn * glint;
            var size = f.Size * (1f - 0.4f * u);
            var box = Box2.CenteredAround(f.Position, new Vector2(size, size * 0.7f));
            args.WorldHandle.DrawRect(new Box2Rotated(box, new Angle(f.Angle), f.Position), f.Colour.WithAlpha(alpha));
        }
    }
}

/// <summary>
/// Reality coming apart round the Pyramid: space swirls and ripples, the picture tears in thin bands and splits into its
/// colours, a little always and violently as each wave leaves or as it collapses.
/// </summary>
public sealed partial class WGPyramidFieldOverlay : Overlay
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IEntityManager _entities = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "WGPyramidField";
    private readonly WGPyramidSystem _system;
    private readonly ShaderInstance _shader;
    private readonly Vector2[] _centres = new Vector2[2];
    private Vector2 _strengths;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public override bool RequestScreenTexture => true;

    public WGPyramidFieldOverlay(WGPyramidSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        ZIndex = 189;
        _shader = _proto.Index(Shader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var transforms = _entities.System<SharedTransformSystem>();
        var count = 0;
        var strengths = new float[2];
        var query = _entities.EntityQueryEnumerator<WGPyramidVisualsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform) && count < 2)
        {
            if (xform.MapID != args.MapId)
                continue;

            var at = transforms.GetWorldPosition(xform);
            if (Vector2.Distance(at, args.WorldAABB.ClosestPoint(at)) > WGPyramidMath.FieldRadius)
                continue;

            _centres[count] = at;
            strengths[count] = _system.Strength(uid);
            count++;
        }

        for (var i = count; i < 2; i++)
        {
            _centres[i] = Vector2.Zero;
            strengths[i] = 0f;
        }

        _strengths = new Vector2(strengths[0], strengths[1]);
        return count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var (right, up) = WGPyramidDraw.Axes(args);
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("viewportMin", args.WorldAABB.BottomLeft);
        _shader.SetParameter("viewportSize", args.WorldAABB.Size);
        _shader.SetParameter("worldToScreenX", right);
        _shader.SetParameter("worldToScreenY", up);
        _shader.SetParameter("centre0", _centres[0]);
        _shader.SetParameter("centre1", _centres[1]);
        _shader.SetParameter("strengths", _strengths);
        _shader.SetParameter("radius", WGPyramidMath.FieldRadius);
        _shader.SetParameter("still", _system.Still);
        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldAABB, Color.White);
        args.WorldHandle.UseShader(null);
    }
}

/// <summary>Where a laser lands: a white flash, a shockwave racing out, and reality breaking into blocks for a moment.</summary>
public sealed partial class WGPyramidImpactOverlay : Overlay
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const int Max = 8;
    private static readonly ProtoId<ShaderPrototype> Shader = "WGPyramidImpact";
    private readonly WGPyramidSystem _system;
    private readonly ShaderInstance _shader;
    private readonly Vector2[] _centres = new Vector2[Max];
    private readonly float[] _ages = new float[Max];

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public override bool RequestScreenTexture => true;

    public WGPyramidImpactOverlay(WGPyramidSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        ZIndex = 190;
        _shader = _proto.Index(Shader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var now = _timing.CurTime;
        var count = 0;
        var view = args.WorldAABB.Enlarged(30f);
        foreach (var (map, at, hit) in _system.Impacts)
        {
            if (count == Max)
                break;

            if (map != args.MapId || !view.Contains(at))
                continue;

            _centres[count] = at;
            _ages[count] = (float) (now - hit).TotalSeconds;
            count++;
        }

        for (var i = count; i < Max; i++)
        {
            _centres[i] = Vector2.Zero;
            _ages[i] = -1f;
        }

        return count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var (right, up) = WGPyramidDraw.Axes(args);
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("viewportMin", args.WorldAABB.BottomLeft);
        _shader.SetParameter("viewportSize", args.WorldAABB.Size);
        _shader.SetParameter("worldToScreenX", right);
        _shader.SetParameter("worldToScreenY", up);
        _shader.SetParameter("centres", _centres);
        _shader.SetParameter("ages", _ages);
        _shader.SetParameter("life", WGPyramidMath.ImpactSeconds);
        _shader.SetParameter("still", _system.Still);
        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldAABB, Color.White);
        args.WorldHandle.UseShader(null);
    }
}
