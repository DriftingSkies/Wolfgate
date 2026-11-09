using System.Numerics;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.Remade.Dread;
using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Lattice;

/// <summary>
/// Shows the hallucinations the server sends this player: figures spawned only on this client, radar contacts that
/// aren't there, and a warped screen that grows with Drift.
/// </summary>
public sealed partial class WFLatticeHallucinationSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>What a glimpse or phantom looks like.</summary>
    private const string FigurePrototype = "WFRemadeGlimpse";

    /// <summary>Blood that isn't there.</summary>
    private const string BloodPrototype = "WFLatticeBloodGhost";

    /// <summary>A person seen as a Remade: who, the Remade standing in for them, and whether they were shown before.</summary>
    private readonly record struct Turned(EntityUid Target, EntityUid Stand, bool WasVisible, TimeSpan Ends);

    private readonly List<Turned> _turned = new();

    /// <summary>A phantom goes when the player comes this close, in metres.</summary>
    private const float VanishRange = 3f;

    private readonly List<(EntityUid Figure, TimeSpan Ends, bool Vanishes)> _figures = new();
    private WFDriftPulseOverlay _pulse = default!;

    /// <summary>Radar contacts that aren't there: where, until when, and a seed for their garbled label.</summary>
    public readonly List<(MapCoordinates Position, TimeSpan Ends, int Seed)> Blips = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFLatticeHallucinationEvent>(OnHallucination);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Clear());
        _overlays.AddOverlay(new WFDriftOverlay());
        _pulse = new WFDriftPulseOverlay();
        _overlays.AddOverlay(_pulse);
        Subs.CVar(_config, CCVars.ReducedMotion, still => _pulse.Still = still, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay<WFDriftOverlay>();
        _overlays.RemoveOverlay(_pulse);
        Clear();
    }

    /// <summary>The local player's Drift level: their ship's or their own, or what they carry in their head if that is more.</summary>
    public int LocalLevel()
    {
        // The Choir see nothing wrong, and the dead are past it.
        if (_player.LocalEntity is { } mob && (HasComp<WFChoirMemberComponent>(mob) || HasComp<GhostComponent>(mob)))
            return 0;

        // What they feel, which the server works out from their ship, charm, sleep and company, or else their ship's.
        if (LocalMind() is { } own)
            return Math.Max(own.FeltLevel, own.MindLevel);

        return LocalDrift()?.Level ?? 0;
    }

    /// <summary>The local player's own Drift, which holds what a Monolith or another source has put in their head.</summary>
    public WFDriftComponent? LocalMind()
    {
        return _player.LocalEntity is { } mob && TryComp<WFDriftComponent>(mob, out var own) ? own : null;
    }

    /// <summary>The Drift the local player carries: their ship's, or their own off a ship.</summary>
    public WFDriftComponent? LocalDrift()
    {
        if (_player.LocalEntity is not { } mob)
            return null;

        if (Transform(mob).GridUid is { } grid && TryComp<WFDriftComponent>(grid, out var ship))
            return ship;

        return TryComp<WFDriftComponent>(mob, out var own) ? own : null;
    }

    private void OnHallucination(WFLatticeHallucinationEvent ev)
    {
        var coordinates = GetCoordinates(ev.Coordinates);
        var ends = _timing.CurTime + TimeSpan.FromSeconds(ev.Seconds);
        switch (ev.Kind)
        {
            case WFLatticeHallucinationKind.Pulse:
                _pulse.Start(ev.Seconds, ev.Seed);
                break;
            case WFLatticeHallucinationKind.Blip:
            case WFLatticeHallucinationKind.Double:
                Blips.Add((_transform.ToMapCoordinates(coordinates), ends, ev.Seed));
                break;
            case WFLatticeHallucinationKind.Glimpse:
            case WFLatticeHallucinationKind.Phantom:
                if (!coordinates.IsValid(EntityManager))
                    return;

                var figure = Spawn(FigurePrototype, coordinates);
                _figures.Add((figure, ends, ev.Kind == WFLatticeHallucinationKind.Phantom));
                break;
            case WFLatticeHallucinationKind.Bleed:
                Scatter(coordinates, BloodPrototype, ev.Seed, 4, 9, 1f, 5f, ends, false);
                break;
            case WFLatticeHallucinationKind.Swarm:
                Scatter(coordinates, FigurePrototype, ev.Seed, 4, 8, 2f, 4.5f, ends, true);
                break;
            case WFLatticeHallucinationKind.Turn:
            case WFLatticeHallucinationKind.Mirror:
                Turn(coordinates.EntityId, ev.Seed, ends, true);
                break;
            case WFLatticeHallucinationKind.Vanish:
                Turn(coordinates.EntityId, ev.Seed, ends, false);
                break;
        }
    }

    /// <summary>Spawns a ring of things round a place, seen only here, until they end.</summary>
    private void Scatter(EntityCoordinates centre, string prototype, int seed, int least, int most, float near, float far, TimeSpan ends, bool remade)
    {
        if (!centre.IsValid(EntityManager))
            return;

        var random = new System.Random(seed);
        var count = random.Next(least, most + 1);
        for (var i = 0; i < count; i++)
        {
            var angle = random.NextDouble() * Math.Tau;
            var distance = near + (far - near) * (float) random.NextDouble();
            var offset = new Vector2((float) Math.Cos(angle), (float) Math.Sin(angle)) * distance;
            var thing = Spawn(prototype, centre.Offset(offset));
            if (remade)
                Dress(thing, random.Next(WFDreadGlimpses.All.Length));
            else if (TryComp<SpriteComponent>(thing, out var sprite))
            {
                sprite.LayerSetState(0, $"splatter-{random.Next(6)}");
                sprite.Rotation = Angle.FromDegrees(random.Next(360));
            }

            _figures.Add((thing, ends, remade));
        }
    }

    /// <summary>Hides something's sprite until it ends, standing a Remade in its place if asked.</summary>
    private void Turn(EntityUid target, int seed, TimeSpan ends, bool remade)
    {
        if (TerminatingOrDeleted(target) || !TryComp<SpriteComponent>(target, out var real) || _turned.Exists(t => t.Target == target))
            return;

        var stand = EntityUid.Invalid;
        if (remade)
        {
            stand = Spawn(FigurePrototype, new EntityCoordinates(target, Vector2.Zero));
            Dress(stand, new System.Random(seed).Next(WFDreadGlimpses.All.Length));
        }

        _turned.Add(new Turned(target, stand, real.Visible, ends));
        real.Visible = false;
    }

    /// <summary>Makes a figure one of the Remade the stone shows.</summary>
    private void Dress(EntityUid figure, int index)
    {
        if (!TryComp<SpriteComponent>(figure, out var sprite))
            return;

        var glimpse = WFDreadGlimpses.All[index];
        sprite.LayerSetRSI(0, new ResPath(glimpse.Rsi));
        sprite.LayerSetState(0, WFDreadGlimpses.State);
        sprite.Offset = new Vector2(0f, WFDreadGlimpses.Offset(glimpse.Height));
    }

    private void Unturn(Turned turned)
    {
        if (turned.Stand.IsValid() && !TerminatingOrDeleted(turned.Stand))
            Del(turned.Stand);

        if (!TerminatingOrDeleted(turned.Target) && TryComp<SpriteComponent>(turned.Target, out var sprite))
            sprite.Visible = turned.WasVisible;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.CurTime;
        Blips.RemoveAll(blip => blip.Ends <= now);
        for (var i = _turned.Count - 1; i >= 0; i--)
        {
            var turned = _turned[i];
            if (now < turned.Ends && !TerminatingOrDeleted(turned.Target) && (!turned.Stand.IsValid() || !TerminatingOrDeleted(turned.Stand)))
                continue;

            Unturn(turned);
            _turned.RemoveAt(i);
        }

        var viewer = _player.LocalEntity;
        for (var i = _figures.Count - 1; i >= 0; i--)
        {
            var (figure, ends, vanishes) = _figures[i];
            var gone = TerminatingOrDeleted(figure);
            var near = vanishes
                       && !gone
                       && viewer is { } mob
                       && _transform.InRange(Transform(figure).Coordinates, Transform(mob).Coordinates, VanishRange);
            if (now < ends && !near && !gone)
                continue;

            if (!gone)
                Del(figure);

            _figures.RemoveAt(i);
        }
    }

    private void Clear()
    {
        foreach (var (figure, _, _) in _figures)
        {
            if (!TerminatingOrDeleted(figure))
                Del(figure);
        }

        _figures.Clear();
        Blips.Clear();
        foreach (var turned in _turned)
        {
            Unturn(turned);
        }

        _turned.Clear();
    }
}
