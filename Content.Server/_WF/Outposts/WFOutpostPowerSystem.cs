using Content.Server._WF.Outposts.Components;
using Content.Server.Atmos.Components;
using Content.Server.Power.Components;
using Content.Shared._WF.Planets;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Light.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Outpost weather power: wind turbines and solar strings set their supply every second from the sky over them, the
/// wind on their map and the planet's time of day.
/// </summary>
public sealed class WFOutpostPowerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedWolfWeatherSystem _weather = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;
        var turbines = EntityQueryEnumerator<WFWindTurbineComponent, PowerSupplierComponent, TransformComponent>();
        while (turbines.MoveNext(out var uid, out var turbine, out var supplier, out var xform))
        {
            supplier.MaxSupply = GetWindSupply((uid, turbine, xform));
        }

        var strings = EntityQueryEnumerator<WFSolarStringComponent, PowerSupplierComponent, TransformComponent>();
        while (strings.MoveNext(out var uid, out var solar, out var supplier, out var xform))
        {
            supplier.MaxSupply = GetSolarSupply((uid, solar, xform));
        }
    }

    /// <summary>What a turbine makes now: nothing under a roof or in thin air, more in a strong wind.</summary>
    public float GetWindSupply(Entity<WFWindTurbineComponent, TransformComponent> turbine)
    {
        var (_, comp, xform) = turbine;
        if (!xform.Anchored || !IsOutdoors(xform) || OutdoorPressure(xform) < comp.MinPressure)
            return 0f;

        var wind = TryComp<WolfWeatherMapComponent>(xform.MapUid, out var weather) ? Speed(weather.Wind) : 0f;
        return comp.Supply * (1f + comp.StormBonus * Math.Clamp(wind / comp.StormWind, 0f, 1f));
    }

    /// <summary>What a solar string makes now: nothing under a roof or at night, most at noon.</summary>
    public float GetSolarSupply(Entity<WFSolarStringComponent, TransformComponent> solar)
    {
        var (_, comp, xform) = solar;
        if (!xform.Anchored || !IsOutdoors(xform))
            return 0f;

        // Off a planet the sun never sets.
        if (!TryComp<WFPlanetEnvironmentComponent>(xform.MapUid, out var environment))
            return comp.Supply;

        if (environment.IsNight)
            return 0f;

        var day = (environment.MinuteOfDay - 6 * 60) / (12f * 60);
        return comp.Supply * MathF.Max(comp.MinDaylight, MathF.Sin(MathF.PI * day));
    }

    /// <summary>Whether weather reaches the tile the entity stands on.</summary>
    public bool IsOutdoors(TransformComponent xform)
    {
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp)
            || !_maps.TryGetTileRef(grid, gridComp, xform.Coordinates, out var tile))
            return false;

        TryComp<RoofComponent>(grid, out var roof);
        return _weather.IsExposed((grid, gridComp, roof), tile);
    }

    private static float Speed(System.Numerics.Vector2 wind)
    {
        return wind.Length();
    }

    private float OutdoorPressure(TransformComponent xform)
    {
        return TryComp<MapAtmosphereComponent>(xform.MapUid, out var atmosphere) ? atmosphere.Mixture.Pressure : 0f;
    }
}
