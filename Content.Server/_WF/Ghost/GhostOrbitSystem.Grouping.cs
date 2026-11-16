using Content.Server._WF.Planets;
using Content.Server.GameTicking;
using Content.Server.Nuke;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Ghost;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.Planets;
using Content.Shared.Mobs.Components;
using Content.Shared.Nuke;
using Content.Shared.Shuttles.Components;
using Robust.Server.Containers;

namespace Content.Server._WF.Ghost;

// The faction and place each target is listed under, for the menu's Faction and Place views.
public sealed partial class GhostOrbitSystem
{
    [Dependency] private ContainerSystem _containers = default!;
    [Dependency] private GameTicker _ticker = default!;

    private const int PlaceSector = 0;
    private const int PlaceWorld = 1;
    private const int PlaceTransit = 2;
    private const int PlaceOther = 3;

    /// <summary>Each map's place for the request being built, so every target on a map shares one lookup.</summary>
    private readonly Dictionary<EntityUid, (string Name, int Order)> _places = new();

    /// <summary>Each world's orbit map, so an orbiting target is listed under its world.</summary>
    private readonly Dictionary<EntityUid, string> _orbitWorlds = new();

    /// <summary>Clears the per-request caches and notes every world's orbit map.</summary>
    private void BeginGrouping()
    {
        _places.Clear();
        _orbitWorlds.Clear();
        var bodies = AllEntityQuery<WFSectorPlanetComponent, MetaDataComponent>();
        while (bodies.MoveNext(out _, out var body, out var meta))
        {
            if (TryGetEntity(body.OrbitMap, out var orbit))
                _orbitWorlds[orbit.Value] = meta.EntityName;
        }
    }

    /// <summary>Fills in a target's faction from the company of the entity or grid that carries one.</summary>
    private void SetFaction(ref GhostOrbitTarget target, EntityUid? holder)
    {
        if (!TryComp<CompanyComponent>(holder, out var company))
            return;

        var family = WFCompanyFamily.Of(company.CompanyName);
        if (family.Length == 0)
            return;

        // A family's own id names it when it is a company (TSF, PDV); otherwise the company itself does.
        var source = _proto.TryIndex<CompanyPrototype>(family, out var root) ? root
            : _proto.TryIndex<CompanyPrototype>(company.CompanyName, out var own) ? own
            : null;
        target.Faction = Loc.TryGetString($"wf-ghost-orbit-faction-{family.ToLowerInvariant()}", out var named)
            ? named
            : source?.Name ?? family;
        target.FactionColor = source?.Color;
    }

    /// <summary>Fills in where a target is: the sector, a world (surface, caves or orbit), or in transit.</summary>
    private void SetPlace(ref GhostOrbitTarget target, TransformComponent xform)
    {
        if (xform.MapUid is not { } map)
            return;

        if (!_places.TryGetValue(map, out var place))
        {
            place = PlaceOf(map);
            _places[map] = place;
        }

        target.Place = place.Name;
        target.PlaceOrder = place.Order;
    }

    private (string Name, int Order) PlaceOf(EntityUid map)
    {
        if (TryComp<WFPlanetLayerComponent>(map, out var layer) && TryGetEntity(layer.Network, out var network)
            && TryComp<WFPlanetNetworkComponent>(network, out var networkComp) && networkComp.Planet is { } body
            && !TerminatingOrDeleted(body))
            return (Name(body), PlaceWorld);

        if (_orbitWorlds.TryGetValue(map, out var world))
            return (world, PlaceWorld);

        if (HasComp<FTLMapComponent>(map) || HasComp<CEZTransitMapComponent>(map))
            return (Loc.GetString("wf-ghost-orbit-place-transit"), PlaceTransit);

        if (Transform(map).MapID == _ticker.DefaultMap)
            return (Loc.GetString("wf-ghost-orbit-place-sector"), PlaceSector);

        return (Name(map), PlaceOther);
    }

    /// <summary>
    /// Whether a point of interest is worth listing now: a nuke only while armed, the disk only out loose or in someone's
    /// hands or pockets. The rest always are.
    /// </summary>
    private bool InterestActive(EntityUid uid)
    {
        if (TryComp<NukeComponent>(uid, out var nuke))
            return nuke.Status == NukeStatus.ARMED;

        if (HasComp<NukeDiskComponent>(uid))
        {
            return !_containers.TryGetOuterContainer(uid, Transform(uid), out var outer)
                   || HasComp<MobStateComponent>(outer.Owner);
        }

        return true;
    }
}
