using System.Numerics;
using Content.Client.Parallax;
using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// What the crew of a ship see of the Coda's leviathan: vast garbled contacts circling at the edge of the radar, then a
/// long dark shape sliding past beneath the hull, its rim faintly violet and two pale eyes at its head.
/// </summary>
public sealed class WFLeviathanSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFLatticeHallucinationSystem _hallucinations = default!;

    private WFLeviathanOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFLeviathanEvent>(OnLeviathan);
        _overlay = new WFLeviathanOverlay(EntityManager, _transform, _timing);
        _overlays.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_overlay);
    }

    private void OnLeviathan(WFLeviathanEvent ev)
    {
        if (!TryGetEntity(ev.Grid, out var grid) || TerminatingOrDeleted(grid))
            return;

        var ends = _timing.CurTime + TimeSpan.FromSeconds(ev.Seconds);
        switch (ev.Phase)
        {
            case WFLeviathanPhase.Approach:
                // A handful of contacts round the ship, too big and too wrong to read.
                var centre = _transform.GetMapCoordinates(grid.Value);
                var random = new System.Random(ev.Seed);
                for (var i = 0; i < 4; i++)
                {
                    var angle = random.NextDouble() * Math.Tau;
                    var distance = 350f + (float) random.NextDouble() * 300f;
                    var at = centre.Position + new Vector2((float) Math.Cos(angle), (float) Math.Sin(angle)) * distance;
                    _hallucinations.Blips.Add((new MapCoordinates(at, centre.MapId), ends, ev.Seed + i));
                }

                break;
            case WFLeviathanPhase.Pass:
                _overlay.Start(grid.Value, ev.Direction, ev.Seconds, ev.Seed);
                break;
        }
    }
}

/// <summary>The leviathan's shape sliding past beneath a ship, drawn under the world.</summary>
public sealed class WFLeviathanOverlay : Overlay
{
    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;

    /// <summary>Body segments, head first, and the metres between them.</summary>
    private const int Segments = 40;
    private const float Spacing = 11f;

    /// <summary>How far either side of the ship it travels.</summary>
    private const float Travel = 380f;

    private EntityUid _grid;
    private Vector2 _direction;
    private TimeSpan _start;
    private float _seconds;
    private float _phase;
    private readonly Vector2[] _points = new Vector2[Segments];

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public WFLeviathanOverlay(IEntityManager entities, SharedTransformSystem transform, IGameTiming timing)
    {
        _entities = entities;
        _transform = transform;
        _timing = timing;
        ZIndex = ParallaxSystem.ParallaxZIndex + 3;
    }

    public void Start(EntityUid grid, Vector2 direction, float seconds, int seed)
    {
        _grid = grid;
        _direction = direction.LengthSquared() > 0f ? Vector2.Normalize(direction) : Vector2.UnitX;
        _start = _timing.CurTime;
        _seconds = MathF.Max(1f, seconds);
        _phase = seed % 100 / 10f;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _grid.IsValid() && !_entities.Deleted(_grid) && Progress() is >= 0f and <= 1f;
    }

    private float Progress()
    {
        return _seconds <= 0f ? 2f : (float) ((_timing.CurTime - _start).TotalSeconds / _seconds);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var progress = Progress();
        var fade = MathF.Min(1f, MathF.Min(progress, 1f - progress) * 5f);
        if (fade <= 0f)
            return;

        var centre = _transform.GetWorldPosition(_grid);
        var side = new Vector2(-_direction.Y, _direction.X);
        var time = (float) _timing.CurTime.TotalSeconds;
        var head = centre + _direction * (MathHelper.Lerp(-Travel, Travel, progress));
        var handle = args.WorldHandle;

        var points = _points;
        for (var i = 0; i < Segments; i++)
        {
            var sway = MathF.Sin(time * 1.1f + _phase + i * 0.32f) * (4f + i * 0.25f);
            points[i] = head - _direction * (i * Spacing) + side * sway;
        }

        // A faint violet rim, then the dark body over it, so it shows where it blots out the stars.
        for (var i = Segments - 1; i >= 0; i--)
        {
            handle.DrawCircle(points[i], Radius(i) + 2.5f, new Color(0.22f, 0.05f, 0.3f, 0.35f * fade));
        }

        for (var i = Segments - 1; i >= 0; i--)
        {
            handle.DrawCircle(points[i], Radius(i), new Color(0.01f, 0f, 0.02f, 0.97f * fade));
        }

        // Two pale eyes at the head, opening slowly.
        var open = 0.5f + 0.5f * MathF.Sin(time * 0.7f + _phase);
        var eyeColour = new Color(0.85f, 0.8f, 0.55f, (0.4f + 0.5f * open) * fade);
        handle.DrawCircle(head + _direction * 7f + side * 5f, 1.4f + open, eyeColour);
        handle.DrawCircle(head + _direction * 7f - side * 5f, 1.4f + open, eyeColour);
    }

    /// <summary>Thick behind the head, tapering to the tail.</summary>
    private static float Radius(int segment)
    {
        var t = segment / (float) (Segments - 1);
        return t < 0.15f ? MathHelper.Lerp(12f, 20f, t / 0.15f) : MathHelper.Lerp(20f, 3f, (t - 0.15f) / 0.85f);
    }
}
