using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server._WF.TradeGoods.Components;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Colonies;

/// <summary>
/// The colony's seat: the ground a developed world's colony stands on. As a world's sites are placed for the round, one
/// at Settled tier or past gets the seat its tier has reached (<c>wfColonySeat</c>): a landing field, then a depot with
/// the colony exchange, then the governor's hall. A depot keeps the colony standing with no outpost on the world, its
/// bunks house people, and some of the colony's people live there.
/// </summary>
public sealed partial class WFColonySystem
{
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedRoofSystem _roof = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WFPlanetPoiSystem _pois = default!;

    /// <summary>The colonist who lives at a seat.</summary>
    public static readonly EntProtoId Colonist = "WFColonyColonist";

    private void InitializeSeat()
    {
        SubscribeLocalEvent<WFPlanetPoiExtraSitesEvent>(OnExtraSites);
        SubscribeLocalEvent<WFPlanetPreloadedEvent>(OnSeatPreloaded);
    }

    /// <summary>The seat a world of a tier gets: the one of the highest tier it has reached.</summary>
    public WFColonySeatPrototype? SeatFor(WFWorldTier tier)
    {
        WFColonySeatPrototype? best = null;
        foreach (var seat in _proto.EnumeratePrototypes<WFColonySeatPrototype>())
        {
            if (seat.Tier <= tier && (best == null || seat.Tier > best.Tier))
                best = seat;
        }

        return best;
    }

    /// <summary>A world's seat this round, on its ground, if one was placed.</summary>
    public bool TryGetSeat(EntityUid network, out Entity<WFColonySeatComponent> seat, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WFColonySeatPrototype? proto)
    {
        seat = default;
        proto = null;
        if (!TryComp<WFPlanetNetworkComponent>(network, out var comp) || !TryComp<WFColonySeatComponent>(comp.GroundMap, out var found)
            || !_proto.TryIndex(found.Seat, out proto))
            return false;

        seat = (comp.GroundMap, found);
        return true;
    }

    /// <summary>Whether a world's seat this round has a depot, which stands for the colony when no outpost does.</summary>
    public bool HasDepot(EntityUid network) => TryGetSeat(network, out _, out var proto) && proto.Tier >= WFWorldTier.Colony;

    /// <summary>Adds the seat a world's tier has reached to the sites placed on it this round.</summary>
    private void OnExtraSites(ref WFPlanetPoiExtraSitesEvent args)
    {
        if (!_cfg.GetCVar(ColonyCVars.Enabled) || !_cfg.GetCVar(ColonyCVars.Seats))
            return;

        if (!Loaded)
        {
            _log.Warning($"World development wasn't read yet when {args.Surface} placed its sites; it gets no colony seat this round.");
            return;
        }

        if (SeatFor(Tier(args.Surface.Id)) is not { } seat)
            return;

        var site = seat.Site;
        args.Sites.Add(new WFPlanetPoiEntry
        {
            MapPath = site.MapPath, Footprint = site.Footprint, Connection = site.Connection, Sockets = site.Sockets,
            Name = site.Name, Expeditions = false, Difficulty = site.Difficulty, MinCount = 1, MaxCount = 1, Priority = true,
        });
    }

    /// <summary>Notes where a world's seat came to stand, and shows it from orbit.</summary>
    private void OnSeatPreloaded(ref WFPlanetPreloadedEvent args)
    {
        if (!TryComp<WFPlanetPoiComponent>(args.Ground, out var record))
            return;

        foreach (var site in record.Sites)
        {
            var seat = _proto.EnumeratePrototypes<WFColonySeatPrototype>().FirstOrDefault(proto => proto.Site.Name == site.Name);
            if (seat == null)
                continue;

            var comp = EnsureComp<WFColonySeatComponent>(args.Ground);
            comp.Seat = seat.ID;
            comp.Bounds = site.Bounds;
            if (TryComp<WFPoiSignalComponent>(site.Signal, out var signal) && !signal.Revealed)
                _pois.Reveal((site.Signal, signal), null);
            return;
        }
    }

    /// <summary>
    /// Keeps a colony's seat in step with it: its exchange trades on the colony's shelf, its hall's beacon shows the
    /// governor's colour, and its colonists are as many as the colony's people call for. One killed is a loss to the colony.
    /// </summary>
    private void UpdateSeat(Entity<WFColonyComponent> colony, WorldState world)
    {
        if (!TryGetSeat(colony.Comp.Network, out var seat, out var proto))
            return;

        var ground = seat.Owner;
        var machines = EntityQueryEnumerator<WFColonyDepotMachineComponent, TransformComponent>();
        while (machines.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == ground)
                EnsureComp<WFTradeMarketLinkComponent>(uid).Market = colony;
        }

        var colour = world.Charter is { } charter && _companies.CompanyIdentity(charter.CompanyId) is { } identity ? identity.Colour : (Color?) null;
        var beacons = EntityQueryEnumerator<WFColonyHallBeaconComponent, TransformComponent>();
        while (beacons.MoveNext(out var uid, out var beacon, out var xform))
        {
            if (xform.GridUid == ground)
                _lights.SetColor(uid, colour ?? beacon.Unchartered);
        }

        var each = MathF.Max(1f, _cfg.GetCVar(ColonyCVars.PeoplePerColonist));
        for (var i = seat.Comp.Colonists.Count - 1; i >= 0; i--)
        {
            var colonist = seat.Comp.Colonists[i];
            if (!TerminatingOrDeleted(colonist) && _mobs.IsAlive(colonist))
                continue;

            seat.Comp.Colonists.RemoveAt(i);
            colony.Comp.People = MathF.Max(0f, colony.Comp.People - each);
        }

        var wanted = Math.Min(proto.Colonists, (int) (colony.Comp.People / each));
        if (seat.Comp.Colonists.Count >= wanted || !TryComp<MapGridComponent>(ground, out var grid))
            return;

        // One at a time, so a seat fills up over a few minutes rather than all at once.
        if (TryFindBunkFloor((ground, grid), seat.Comp.Bounds) is { } tile)
            seat.Comp.Colonists.Add(Spawn(Colonist, new EntityCoordinates(ground, (Vector2) tile + new Vector2(0.5f))));
    }

    /// <summary>A free tile of roofed floor inside a seat, at random.</summary>
    private Vector2i? TryFindBunkFloor(Entity<MapGridComponent> ground, Box2i bounds)
    {
        if (!TryComp<RoofComponent>(ground, out var roof))
            return null;

        var free = new List<Vector2i>();
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            for (var y = bounds.Bottom; y < bounds.Top; y++)
            {
                var tile = new Vector2i(x, y);
                if (_roof.IsRooved((ground.Owner, ground.Comp, roof), tile) && _maps.TryGetTileRef(ground, ground.Comp, tile, out var tileRef)
                    && !tileRef.Tile.IsEmpty && !_turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
                    free.Add(tile);
            }
        }

        return free.Count == 0 ? null : _random.Pick(free);
    }
}
