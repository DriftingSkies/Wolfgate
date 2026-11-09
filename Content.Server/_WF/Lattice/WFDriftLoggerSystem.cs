using Content.Server.Cargo.Systems;
using Content.Server.Shuttles.Systems;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Lattice;
using Content.Shared.Examine;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// Drift loggers count readings while a player carries them out past the Lattice's hold, and sell for them. A ship
/// moving a Monolith drags its whole crew's Drift up and can't hide its name on radar.
/// </summary>
public sealed class WFDriftLoggerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private WFDriftMindSystem _mind = default!;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);

    /// <summary>Drift a second a moving Monolith puts in the head of everyone aboard its ship.</summary>
    private const float CourierDrift = 0.25f;

    /// <summary>How fast a ship has to be moving for its Monolith to count as carried, in metres a second.</summary>
    private const float CourierSpeed = 1f;

    private TimeSpan _next;
    private readonly HashSet<EntityUid> _courierShips = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFDriftLoggerComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFDriftLoggerComponent, PriceCalculationEvent>(OnPrice);
    }

    private void OnExamined(Entity<WFDriftLoggerComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-drift-logger-examine", ("readings", (int) ent.Comp.Readings)));
    }

    private void OnPrice(Entity<WFDriftLoggerComponent> ent, ref PriceCalculationEvent args)
    {
        args.Price += ent.Comp.Readings * ent.Comp.PricePerReading;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Tick;
        var minutes = (float) Tick.TotalMinutes;
        Log(minutes);
        Couriers((float) Tick.TotalSeconds);
    }

    /// <summary>Each logger carried by a player counts readings for the band they are in.</summary>
    private void Log(float minutes)
    {
        var loggers = EntityQueryEnumerator<WFDriftLoggerComponent>();
        while (loggers.MoveNext(out var uid, out var logger))
        {
            if (logger.Readings >= logger.MaxReadings || Carrier(uid) is not { } person)
                continue;

            var band = Band(person);
            if (band <= 0 || band >= logger.PerMinute.Count)
                continue;

            logger.Readings = Math.Min(logger.MaxReadings, logger.Readings + logger.PerMinute[band] * minutes);
        }
    }

    /// <summary>A ship moving a Monolith drags its crew's Drift up, and its name shows on every radar.</summary>
    private void Couriers(float seconds)
    {
        _courierShips.Clear();
        var stones = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (stones.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is not { } grid
                || HasComp<MapComponent>(grid)
                || !HasComp<ShuttleComponent>(grid)
                || !TryComp<PhysicsComponent>(grid, out var body)
                || body.LinearVelocity.Length() < CourierSpeed
                || !_courierShips.Add(grid))
                continue;

            _shuttle.RemoveIFFFlag(grid, IFFFlags.HideLabel | IFFFlags.HideLabelAlways);
        }

        if (_courierShips.Count == 0)
            return;

        var crew = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (crew.MoveNext(out var person, out _, out var personXform))
        {
            if (personXform.GridUid is { } on && _courierShips.Contains(on))
                _mind.Add(person, CourierDrift * seconds, WFDriftFlavour.Monolith);
        }
    }

    /// <summary>The player carrying something, however deep in bags or hands.</summary>
    private EntityUid? Carrier(EntityUid thing)
    {
        var current = thing;
        while (_container.TryGetContainingContainer((current, null, null), out var container))
        {
            current = container.Owner;
            if (HasComp<ActorComponent>(current))
                return current;
        }

        return null;
    }

    private int Band(EntityUid person)
    {
        if (Transform(person).GridUid is { } grid && TryComp<WFDriftComponent>(grid, out var ship))
            return ship.Band;

        return CompOrNull<WFDriftComponent>(person)?.Band ?? 0;
    }
}
