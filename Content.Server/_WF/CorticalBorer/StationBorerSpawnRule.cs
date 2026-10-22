using Content.Server._NF.SectorServices;
using Content.Server._NF.Station.Components;
using Content.Server.StationEvents.Components;
using Content.Server.StationEvents.Events;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.CorticalBorer;

/// <summary>
/// Vent borer event that only picks stations. Vent events pick from event-eligible stations, which are the ships.
/// </summary>
public sealed class StationBorerSpawnRule : StationEventSystem<StationBorerSpawnRuleComponent>
{
    protected override void Started(EntityUid uid, StationBorerSpawnRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var locations = GetSpawnLocations();
        if (locations.Count == 0)
            return;

        var vents = RobustRandom.Pick(locations).Value;
        RobustRandom.Shuffle(vents);

        var count = Math.Min(component.Count.Next(RobustRandom), vents.Count);
        for (var i = 0; i < count; i++)
        {
            Spawn(component.Spawn, vents[i]);
        }
    }

    /// <summary>
    /// Vent spawn points by station, leaving out ships and the central station.
    /// </summary>
    public Dictionary<EntityUid, List<EntityCoordinates>> GetSpawnLocations()
    {
        var result = new Dictionary<EntityUid, List<EntityCoordinates>>();
        var query = EntityQueryEnumerator<VentCritterSpawnLocationComponent, TransformComponent>();
        while (query.MoveNext(out _, out var xform))
        {
            if (xform.GridUid is not { } grid
                || HasComp<ShuttleDeedComponent>(grid)
                || CompOrNull<StationMemberComponent>(grid)?.Station is not { Valid: true } station
                || !IsBorerStation(station))
            {
                continue;
            }

            if (!result.TryGetValue(station, out var list))
                result[station] = list = new List<EntityCoordinates>();

            list.Add(xform.Coordinates);
        }

        return result;
    }

    /// <summary>
    /// Whether borers may spawn on a station: not a vessel and not the sector's service host.
    /// </summary>
    public bool IsBorerStation(EntityUid station)
    {
        return !HasComp<ExtraShuttleInformationComponent>(station)
            && !HasComp<StationSectorServiceHostComponent>(station);
    }
}
