using Content.Server._WG.GroundCover;
using Content.Server._WG.Planets;
using Content.Server.Power.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WG.Farming;

/// <summary>How a collector gets its water, which decides the worlds it suits.</summary>
public enum WGWaterSource : byte
{
    /// <summary>Catches rain under an open sky: free, but only on wet worlds while it rains.</summary>
    Rain,

    /// <summary>Condenses water out of the air on power: best on hot worlds, nothing below freezing.</summary>
    Vapour,

    /// <summary>Melts snowfall and frost on power: for frozen worlds.</summary>
    Snow,
}

/// <summary>A water collector: rain catcher, moisture vaporator or snow melter.</summary>
[RegisterComponent, Access(typeof(WGWaterCollectorSystem))]
public sealed partial class WGWaterCollectorComponent : Component
{
    [DataField(required: true)]
    public WGWaterSource Source;

    /// <summary>Units of water a second at its best.</summary>
    [DataField]
    public float Rate = 0.5f;

    [DataField]
    public string Solution = "tank";

    /// <summary>Sprinklers within this many tiles are kept topped up from its tank.</summary>
    [DataField]
    public float Reach = 6f;

    /// <summary>The share of its best rate it last ran at, for examine.</summary>
    [ViewVariables]
    public float Efficiency;
}

/// <summary>Fills water collectors from their world's weather and tops up nearby sprinklers.</summary>
public sealed class WGWaterCollectorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedRoofSystem _roofs = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private WGGroundCoverSystem _weather = default!;
    [Dependency] private WGPlanetClimateSystem _climate = default!;

    private const string Water = "Water";
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGWaterCollectorComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;
        _next = _timing.CurTime + Tick;
        var seconds = (float) Tick.TotalSeconds;

        var query = EntityQueryEnumerator<WGWaterCollectorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var collector, out var xform))
        {
            collector.Efficiency = xform.Anchored ? Efficiency(uid, collector, xform) : 0f;
            if (!_solutions.TryGetSolution(uid, collector.Solution, out var tank, out _))
                continue;

            if (collector.Efficiency > 0f)
                _solutions.TryAddReagent(tank.Value, Water, FixedPoint2.New(collector.Rate * collector.Efficiency * seconds));

            Share(uid, collector, tank.Value);
        }
    }

    /// <summary>The share of its best rate a collector runs at where it stands.</summary>
    private float Efficiency(EntityUid uid, WGWaterCollectorComponent collector, TransformComponent xform)
    {
        if (xform.MapUid is not { } map)
            return 0f;

        var celsius = _climate.GetOutsideTemperature(map) is { } kelvin ? kelvin - 273.15f : 20f;
        switch (collector.Source)
        {
            case WGWaterSource.Rain:
                if (Roofed(uid, xform))
                    return 0f;
                return Math.Clamp(_weather.GetPrecipitation(map).Rain, 0f, 1f);

            case WGWaterSource.Vapour:
                if (!_power.IsPowered(uid) || celsius <= 0f)
                    return 0f;
                return Math.Clamp(celsius / 35f, 0.05f, 1f);

            case WGWaterSource.Snow:
                if (!_power.IsPowered(uid))
                    return 0f;
                var snow = Math.Clamp(_weather.GetPrecipitation(map).Snow, 0f, 1f);
                var frost = celsius < 0f ? Math.Clamp(-celsius / 30f, 0.2f, 0.6f) : 0f;
                return Math.Clamp(snow + frost, 0f, 1f);
        }

        return 0f;
    }

    private bool Roofed(EntityUid uid, TransformComponent xform)
    {
        return xform.GridUid is { } grid && TryComp<MapGridComponent>(grid, out var gridComp) && TryComp<RoofComponent>(grid, out var roof)
            && _roofs.IsRooved((grid, gridComp, roof), _maps.TileIndicesFor(grid, gridComp, xform.Coordinates));
    }

    /// <summary>Tops up the sprinklers within reach from the collector's tank.</summary>
    private void Share(EntityUid uid, WGWaterCollectorComponent collector, Entity<Content.Shared.Chemistry.Components.SolutionComponent> tank)
    {
        foreach (var sprinkler in _lookup.GetEntitiesInRange<WGSprinklerComponent>(Transform(uid).Coordinates, collector.Reach))
        {
            if (!_solutions.TryGetSolution(sprinkler.Owner, sprinkler.Comp.Solution, out var target, out var targetSolution))
                continue;

            var room = targetSolution.AvailableVolume;
            var have = tank.Comp.Solution.GetTotalPrototypeQuantity(Water);
            var move = FixedPoint2.Min(room, have);
            if (move <= FixedPoint2.Zero)
                continue;

            _solutions.RemoveReagent(tank, Water, move);
            _solutions.TryAddReagent(target.Value, Water, move);
        }
    }

    private void OnExamined(Entity<WGWaterCollectorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var percent = (int) (ent.Comp.Efficiency * 100);
        args.PushMarkup(Loc.GetString(percent > 0 ? "wg-water-collector-running" : "wg-water-collector-idle-" + ent.Comp.Source.ToString().ToLowerInvariant(),
            ("percent", percent)));
    }
}
