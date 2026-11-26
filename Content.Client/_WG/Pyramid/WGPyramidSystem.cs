using System.Numerics;
using Content.Client._WG.CodaSong;
using Content.Shared._WG.Pyramid;
using Content.Shared.CCVar;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WG.Pyramid;

/// <summary>
/// Everything the Pyramid does to the screen. Fragments peel off it and rise, in a steady stream and in great surges
/// as each wave leaves; reality swirls and tears round it, hardest as a wave goes; waves bend space as they run out
/// and knock the world off its hinges as they pass you; tethers reach for each mark as a barrage charges; lasers land
/// in shockwaves of broken pixels; and when it is brought down it shudders, drags its fragments in, and folds away.
/// </summary>
public sealed partial class WGPyramidSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCodaSongSystem _song = default!;

    /// <summary>A wave in flight and whether it has reached this client's view yet.</summary>
    public sealed class Wave(WGPyramidWaveEvent ev, TimeSpan left)
    {
        public readonly WGPyramidWaveEvent Event = ev;
        public readonly TimeSpan Left = left;
        public bool Reached;
    }

    /// <summary>One fragment of chrome in flight.</summary>
    public struct Fragment
    {
        public MapId Map;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Age;
        public float Life;
        public float Size;
        public float Angle;
        public float Spin;
        public Color Colour;

        /// <summary>A breathing flake belongs to a Pyramid: it lifts off its hull and settles back, following it.</summary>
        public EntityUid? Owner;
        public Vector2 Rest;
        public Vector2 Out;
    }

    /// <summary>What each Pyramid on screen is doing.</summary>
    public sealed class State
    {
        public TimeSpan LastWave = TimeSpan.Zero;
        public TimeSpan? CollapseStart;
        public float CollapseSeconds;
        public float Emit;
    }

    public readonly List<Wave> Waves = new();
    public readonly List<(WGPyramidBeamEvent Beam, TimeSpan Fired)> Beams = new();
    public readonly List<(MapId Map, Vector2 At, TimeSpan Hit)> Impacts = new();
    public readonly List<Fragment> Fragments = new();
    public readonly Dictionary<EntityUid, State> States = new();
    public readonly Dictionary<EntityUid, TimeSpan> MarkBirths = new();
    private readonly Dictionary<EntityUid, float> _charges = new();
    private readonly Dictionary<EntityUid, Vector2> _positions = new();

    private const int MaxFragments = 1400;
    private static readonly Color[] Palette =
    {
        Color.FromHex("#96dcd2"), Color.FromHex("#bee6a0"), Color.FromHex("#e8f2fc"), Color.FromHex("#78b4dc"),
        Color.FromHex("#d2c878"), Color.FromHex("#c896af"),
    };

    // The pyramid's outline in metres in screen space, from the sprite: apex, base corners and the front corner.
    private static readonly Vector2 Apex = new(0f, 10.5f);
    private static readonly Vector2 Left = new(-10.5f, -6.5f);
    private static readonly Vector2 Right = new(10.5f, -6.5f);
    private static readonly Vector2 FrontCorner = new(0f, -9.5f);

    private WGPyramidWaveOverlay _waveOverlay = default!;
    private WGPyramidWarpOverlay _warpOverlay = default!;
    private WGPyramidBeamOverlay _beamOverlay = default!;
    private WGPyramidFieldOverlay _fieldOverlay = default!;
    private WGPyramidImpactOverlay _impactOverlay = default!;
    private WGPyramidFragmentOverlay _fragmentOverlay = default!;

    /// <summary>True with reduced motion: no tearing, swirling or shaking, fewer fragments.</summary>
    public bool Still;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGPyramidWaveEvent>(OnWave);
        SubscribeNetworkEvent<WGPyramidBeamEvent>(OnBeam);
        SubscribeNetworkEvent<WGPyramidCollapseEvent>(OnCollapse);
        SubscribeLocalEvent<WGPyramidMarkComponent, ComponentStartup>((uid, _, _) => MarkBirths[uid] = _timing.CurTime);
        SubscribeLocalEvent<WGPyramidMarkComponent, ComponentShutdown>((uid, _, _) => MarkBirths.Remove(uid));
        SubscribeLocalEvent<WGPyramidVisualsComponent, ComponentShutdown>((uid, _, _) =>
        {
            States.Remove(uid);
            _positions.Remove(uid);
        });

        _waveOverlay = new WGPyramidWaveOverlay(this);
        _warpOverlay = new WGPyramidWarpOverlay();
        _beamOverlay = new WGPyramidBeamOverlay(this);
        _fieldOverlay = new WGPyramidFieldOverlay(this);
        _impactOverlay = new WGPyramidImpactOverlay(this);
        _fragmentOverlay = new WGPyramidFragmentOverlay(this);
        foreach (var overlay in Overlays())
        {
            _overlays.AddOverlay(overlay);
        }

        Subs.CVar(_config, CCVars.ReducedMotion, still =>
        {
            Still = still;
            _warpOverlay.Still = still;
        }, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        foreach (var overlay in Overlays())
        {
            _overlays.RemoveOverlay(overlay);
        }
    }

    private Overlay[] Overlays()
    {
        return new Overlay[] { _fragmentOverlay, _beamOverlay, _fieldOverlay, _impactOverlay, _waveOverlay, _warpOverlay };
    }

    /// <summary>How far a wave's front has run, in metres.</summary>
    public float Front(Wave wave)
    {
        return (float) (_timing.CurTime - wave.Left).TotalSeconds * wave.Event.Speed;
    }

    /// <summary>
    /// How hard reality is bending round a Pyramid right now: a little always, a spike as each wave leaves, and climbing
    /// as it collapses.
    /// </summary>
    public float Strength(EntityUid uid)
    {
        if (!States.TryGetValue(uid, out var state))
            return 0.25f;

        var now = _timing.CurTime;
        var pulse = MathF.Exp(-(float) (now - state.LastWave).TotalSeconds / 0.9f);
        var collapse = state.CollapseStart is { } start
            ? MathF.Min(1f, (float) (now - start).TotalSeconds / MathF.Max(0.1f, state.CollapseSeconds)) * 2.2f
            : 0f;
        return 0.25f + pulse + collapse;
    }

    /// <summary>How far through its charge a mark is, 0 to 1.</summary>
    public float Charge(EntityUid mark)
    {
        if (TryComp<WGPyramidMarkComponent>(mark, out var comp) && comp.FireAt != TimeSpan.Zero && comp.ChargeSeconds > 0f)
            return Math.Clamp(1f - (float) (comp.FireAt - _timing.CurTime).TotalSeconds / comp.ChargeSeconds, 0f, 1f);

        return MarkBirths.TryGetValue(mark, out var born)
            ? Math.Clamp((float) (_timing.CurTime - born).TotalSeconds / WGPyramidMath.ChargeSeconds, 0f, 1f)
            : 0f;
    }

    /// <summary>Which way is up on the screen, in world space.</summary>
    public Vector2 ScreenUp => _eye.CurrentEye.Rotation.RotateVec(Vector2.UnitY);

    private State StateOf(EntityUid uid)
    {
        if (!States.TryGetValue(uid, out var state))
            States[uid] = state = new State();

        return state;
    }

    private void OnWave(WGPyramidWaveEvent ev)
    {
        var now = _timing.CurTime;
        Waves.Add(new Wave(ev, now));
        if (TryGetEntity(ev.Source, out var source))
            StateOf(source.Value).LastWave = now;

        // A surge of fragments torn off and flung upward with the wave.
        Burst(ev.Map, ev.Origin, ev.Strength > 1f ? 900 : 320, ev.Strength > 1f ? 2.2f : 1f);
    }

    private void OnBeam(WGPyramidBeamEvent ev)
    {
        var now = _timing.CurTime;
        Beams.Add((ev, now));
        foreach (var to in ev.To)
        {
            Impacts.Add((ev.Map, to, now));
            Spray(ev.Map, to, 40);
        }
    }

    private void OnCollapse(WGPyramidCollapseEvent ev)
    {
        if (TryGetEntity(ev.Pyramid, out var pyramid))
        {
            var state = StateOf(pyramid.Value);
            state.CollapseStart = _timing.CurTime;
            state.CollapseSeconds = ev.Seconds;
        }

        var eye = _eye.CurrentEye.Position;
        if (eye.MapId == ev.Map && Vector2.Distance(eye.Position, ev.At) < 160f)
            _warpOverlay.Start(_timing.CurTime, 1.2f);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var now = _timing.CurTime;
        Waves.RemoveAll(w => Front(w) > w.Event.Reach + WGPyramidMath.WaveWidth * 2f);
        Beams.RemoveAll(b => (now - b.Fired).TotalSeconds > WGPyramidMath.BeamSeconds);
        Impacts.RemoveAll(i => (now - i.Hit).TotalSeconds > WGPyramidMath.ImpactSeconds);

        var eye = _eye.CurrentEye.Position;
        foreach (var wave in Waves)
        {
            if (wave.Reached || wave.Event.Map != eye.MapId)
                continue;

            var distance = Vector2.Distance(eye.Position, wave.Event.Origin);
            if (distance > wave.Event.Reach || distance > Front(wave))
                continue;

            wave.Reached = true;
            var felt = wave.Event.Strength * (1f - 0.6f * distance / wave.Event.Reach);
            _warpOverlay.Start(now, felt);
            // The song in the listener's head turns as the wave goes through it.
            _song.Disturb(4f, Math.Clamp(felt, 0f, 1f));
        }

        UpdatePyramids(frameTime, now);
        UpdateFragments(frameTime);
    }

    /// <summary>Drives each Pyramid's sprite and keeps its fragments streaming.</summary>
    private void UpdatePyramids(float frameTime, TimeSpan now)
    {
        // Each Pyramid's lamps follow its own marks only.
        _charges.Clear();
        var marks = EntityQueryEnumerator<WGPyramidMarkComponent>();
        while (marks.MoveNext(out var mark, out var markComp))
        {
            if (markComp.Pyramid is { } owner)
                _charges[owner] = MathF.Max(_charges.GetValueOrDefault(owner), Charge(mark));
        }

        var query = EntityQueryEnumerator<WGPyramidVisualsComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            var state = StateOf(uid);
            var map = _transform.GetMapCoordinates(uid, xform);
            _positions[uid] = map.Position;
            var strength = Strength(uid);
            Entity<SpriteComponent?> ent = (uid, sprite);

            // The aura flares as each wave leaves; the lamps burn hotter while a barrage charges.
            _sprite.LayerSetColor(ent, "aura", Color.White.WithAlpha(Math.Clamp(0.3f + 0.6f * (strength - 0.25f), 0f, 1f)));
            _sprite.LayerSetColor(ent, "lights", Color.White.WithAlpha(0.6f + 0.4f * _charges.GetValueOrDefault(uid)));

            if (state.CollapseStart is { } start)
            {
                var u = Math.Clamp((float) (now - start).TotalSeconds / MathF.Max(0.1f, state.CollapseSeconds), 0f, 1f);
                // It shudders harder and harder, flickers, then folds in on itself.
                var shake = Still ? 0f : 0.15f + 0.9f * u;
                _sprite.SetOffset(ent, new Vector2(_random.NextFloat(-shake, shake), _random.NextFloat(-shake, shake)));
                var fold = u < 0.85f ? 1f : 1f - (u - 0.85f) / 0.15f;
                _sprite.SetScale(ent, new Vector2(4f * (0.6f + 0.4f * fold), 4f * MathF.Max(0.02f, fold)));
                var flicker = Color.White.WithAlpha(_random.Prob(0.12f + 0.4f * u) ? 0.35f : 1f);
                _sprite.LayerSetColor(ent, "body", flicker);
                _sprite.LayerSetColor(ent, "skin", flicker);
                // Its fragments are dragged back in.
                Implode(map.MapId, map.Position, (int) MathF.Ceiling(frameTime * (60 + 400 * u)));
            }

            // Flakes breathe off the hull and settle back, as in the reference; a few tear away and rise.
            state.Emit += frameTime * (Still ? 30f : 90f);
            while (state.Emit >= 1f)
            {
                state.Emit -= 1f;
                if (_random.Prob(0.8f))
                    Breathe(uid, map.MapId);
                else
                    Peel(map.MapId, map.Position);
            }
        }
    }

    private void UpdateFragments(float frameTime)
    {
        var up = ScreenUp;
        var drag = MathF.Pow(0.82f, frameTime);
        for (var i = Fragments.Count - 1; i >= 0; i--)
        {
            var f = Fragments[i];
            f.Age += frameTime;
            if (f.Age >= f.Life)
            {
                Fragments[i] = Fragments[^1];
                Fragments.RemoveAt(Fragments.Count - 1);
                continue;
            }

            if (f.Owner is { } owner)
            {
                // A breathing flake rises off its rest on the hull and sinks back to it, following the Pyramid.
                if (!_positions.TryGetValue(owner, out var centre))
                {
                    f.Age = f.Life;
                    Fragments[i] = f;
                    continue;
                }

                var lift = MathF.Sin(MathF.PI * f.Age / f.Life);
                f.Position = centre + ToWorld(f.Rest + f.Out * lift, up);
                f.Angle += f.Spin * frameTime;
                Fragments[i] = f;
                continue;
            }

            // They keep climbing as they go, and slow a little.
            f.Velocity = (f.Velocity + up * 1.6f * frameTime) * drag;
            f.Position += f.Velocity * frameTime;
            f.Angle += f.Spin * frameTime;
            Fragments[i] = f;
        }
    }

    /// <summary>A random point on the pyramid's faces, in screen-space metres from its centre.</summary>
    private Vector2 OnFaces()
    {
        var (a, b, c) = _random.Prob(0.5f) ? (Apex, Left, FrontCorner) : (Apex, FrontCorner, Right);
        var u = _random.NextFloat();
        var v = _random.NextFloat();
        if (u + v > 1f)
        {
            u = 1f - u;
            v = 1f - v;
        }

        return a + (b - a) * u + (c - a) * v;
    }

    private static Vector2 ToWorld(Vector2 screen, Vector2 up)
    {
        var right = new Vector2(up.Y, -up.X);
        return right * screen.X + up * screen.Y;
    }

    private void Add(Fragment fragment)
    {
        if (Fragments.Count < MaxFragments)
            Fragments.Add(fragment);
    }

    private Fragment Make(MapId map, Vector2 at, Vector2 velocity, float life, float size)
    {
        return new Fragment
        {
            Map = map, Position = at, Velocity = velocity, Life = life, Size = size,
            Angle = _random.NextFloat(0f, MathF.Tau), Spin = _random.NextFloat(-6f, 6f), Colour = _random.Pick(Palette),
        };
    }

    /// <summary>A flake lifting off the hull a few metres, away from the Pyramid's heart, and settling back.</summary>
    private void Breathe(EntityUid owner, MapId map)
    {
        var rest = OnFaces();
        var away = rest - new Vector2(0f, 1.5f);
        var direction = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : Vector2.UnitY;
        var fragment = Make(map, Vector2.Zero, Vector2.Zero, _random.NextFloat(1.2f, 2.6f), _random.NextFloat(0.1f, 0.3f));
        fragment.Owner = owner;
        fragment.Rest = rest;
        fragment.Out = direction * _random.NextFloat(1.5f, 6f);
        Add(fragment);
    }

    /// <summary>One fragment tearing free of a face and drifting up and out.</summary>
    private void Peel(MapId map, Vector2 centre)
    {
        var up = ScreenUp;
        var local = OnFaces();
        var side = local.X < 0 ? -1f : 1f;
        var velocity = new Vector2(side * _random.NextFloat(0.4f, 2.2f), _random.NextFloat(1.5f, 4.5f));
        Add(Make(map, centre + ToWorld(local, up), ToWorld(velocity, up), _random.NextFloat(2.5f, 5f), _random.NextFloat(0.12f, 0.38f)));
    }

    /// <summary>A wave of fragments: torn from every face and the base at once and flung upward and outward.</summary>
    private void Burst(MapId map, Vector2 centre, int count, float force)
    {
        var up = ScreenUp;
        if (Still)
            count /= 3;

        for (var i = 0; i < count; i++)
        {
            Vector2 local, velocity;
            if (_random.Prob(0.6f))
            {
                local = OnFaces();
                var side = local.X < 0 ? -1f : 1f;
                velocity = new Vector2(side * _random.NextFloat(1f, 6f), _random.NextFloat(7f, 15f));
            }
            else
            {
                // Off the base, fanning out wide before rising.
                var t = _random.NextFloat();
                local = _random.Prob(0.5f) ? Left + (FrontCorner - Left) * t : FrontCorner + (Right - FrontCorner) * t;
                var side = local.X < 0 ? -1f : 1f;
                velocity = new Vector2(side * _random.NextFloat(6f, 16f), _random.NextFloat(0.5f, 5f));
            }

            Add(Make(map, centre + ToWorld(local, up), ToWorld(velocity * force, up), _random.NextFloat(2f, 4.5f),
                _random.NextFloat(0.15f, 0.5f)));
        }
    }

    /// <summary>Chips of hull and reality thrown up from a laser hit.</summary>
    private void Spray(MapId map, Vector2 at, int count)
    {
        var up = ScreenUp;
        for (var i = 0; i < count; i++)
        {
            var dir = _random.NextAngle().ToVec();
            Add(Make(map, at, dir * _random.NextFloat(3f, 11f) + up * _random.NextFloat(1f, 5f), _random.NextFloat(0.6f, 1.6f),
                _random.NextFloat(0.1f, 0.3f)));
        }
    }

    /// <summary>Fragments rushing in from all round as it collapses.</summary>
    private void Implode(MapId map, Vector2 centre, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var dir = _random.NextAngle().ToVec();
            var distance = _random.NextFloat(14f, 30f);
            var speed = _random.NextFloat(14f, 26f);
            Add(Make(map, centre + dir * distance, -dir * speed, distance / speed, _random.NextFloat(0.15f, 0.45f)));
        }
    }
}
