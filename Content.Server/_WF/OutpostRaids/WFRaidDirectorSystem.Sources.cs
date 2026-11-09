using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.OutpostRaids;
using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.OutpostRaids;

/// <summary>
/// Where raids come from. A pack with a <see cref="WFRaidSource"/> is only sent while its source stands: a camp's
/// landing parties while any of its crew lives, the Reavers' shuttles while a stronghold is in reach of the world.
/// Clearing the camp or breaking the stronghold ends those raids.
/// </summary>
public sealed partial class WFRaidDirectorSystem
{
    [Dependency] private WFReaverCampaignSystem _reavers = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    /// <summary>Degrees either side of its camp's bearing a camp's raid walks in from.</summary>
    private const float CampArc = 35f;

    private readonly HashSet<string> _livingGroups = new();

    /// <summary>Whether a pack's source stands, so it can be sent at an outpost now.</summary>
    public bool SourceStands(WFRaidPack pack, Entity<WFOutpostComponent> outpost)
    {
        return pack.Source switch
        {
            WFRaidSource.Camp => TryGetRaidCamp(outpost, out _),
            WFRaidSource.Reavers => ReaversInReach(outpost.Comp.Ground),
            WFRaidSource.Drifters => !_reavers.Running,
            _ => true,
        };
    }

    /// <summary>The nearest camp on the outpost's world that raids and has someone left alive, as a world position.</summary>
    public bool TryGetRaidCamp(Entity<WFOutpostComponent> outpost, out Vector2 position)
    {
        position = default;
        var ground = outpost.Comp.Ground;
        if (TerminatingOrDeleted(ground))
            return false;

        _livingGroups.Clear();
        var crews = EntityQueryEnumerator<WFCrewComponent>();
        while (crews.MoveNext(out var uid, out var crew))
        {
            if (crew.Group.Length > 0 && _mobState.IsAlive(uid))
                _livingGroups.Add(crew.Group);
        }

        var map = Transform(ground).MapID;
        var from = _transform.GetWorldPosition(outpost);
        var best = float.MaxValue;
        var camps = EntityQueryEnumerator<WFPlanetCampComponent, TransformComponent>();
        while (camps.MoveNext(out _, out var camp, out var xform))
        {
            if (xform.MapID != map || !_livingGroups.Contains(camp.Group)
                || !_proto.TryIndex(camp.Camp, out var proto) || !proto.Raids)
                continue;

            var at = _transform.GetWorldPosition(xform);
            var distance = (at - from).LengthSquared();
            if (distance >= best)
                continue;

            best = distance;
            position = at;
        }

        return best < float.MaxValue;
    }

    /// <summary>Whether the Reaver campaign runs and has a stronghold within reach of a ground's world.</summary>
    public bool ReaversInReach(EntityUid ground)
    {
        if (!_reavers.Running || _reavers.State() is not { Finished: false } || !_territory.Enabled
            || !_weather.TryGetWorld(ground, out var world)
            || CompOrNull<WFPlanetNetworkComponent>(world)?.Planet is not { } planet || TerminatingOrDeleted(planet)
            || Transform(planet).MapID != _territory.SectorMap)
            return false;

        var cell = _territory.CellOf(_transform.GetMapCoordinates(planet));
        var reach = _cfg.GetCVar(OutpostRaidCVars.ReaverReach);
        foreach (var stronghold in _reavers.Strongholds())
        {
            if (WFSectorHex.Distance(stronghold.Comp1.Cell, cell) <= reach)
                return true;
        }

        return false;
    }
}
