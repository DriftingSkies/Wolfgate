using System.Linq;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._WF.Contracts;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.Contracts;

/// <summary>Planetside contracts: a free site on one of the sector's worlds instead of a wreck, armed with the world's own wildlife.</summary>
public sealed partial class WFContractSystem
{
    [Dependency] private WFPlanetPoiSystem _pois = default!;

    /// <summary>Worlds no contract is ever posted on. Carcinoma is a quarantined death world.</summary>
    public static readonly HashSet<string> ExcludedWorlds = new() { "WFSurfaceCarcinoma" };

    /// <summary>Tiles past a site's bounds that still count as setting a kit down on it.</summary>
    private const int KitMargin = 6;

    /// <summary>How close a player must be to a released site to keep what is left on it.</summary>
    private const float SiteKeepRange = 64f;

    /// <summary>Ground maps of worlds a planetside objective may use now, with a free site, by surface.</summary>
    private List<(EntityUid Ground, string World)> PlanetGrounds(WFContractPlanet planet)
    {
        var grounds = new List<(EntityUid, string)>();
        var networks = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (networks.MoveNext(out var network))
        {
            string world = network.Surface;
            if (ExcludedWorlds.Contains(world) || planet.Worlds.Count > 0 && !planet.Worlds.Any(w => w.Id == world)
                || FaunaFor(world) == null || !TryComp<WFPlanetPoiComponent>(network.GroundMap, out var record)
                || !record.NetworkReady || FreeSite(record) == null)
                continue;

            grounds.Add((network.GroundMap, world));
        }

        return grounds;
    }

    /// <summary>The world's fauna pool; a world without one takes no planetside contracts.</summary>
    private WFContractWorldFaunaPrototype? FaunaFor(string world)
    {
        return _proto.EnumeratePrototypes<WFContractWorldFaunaPrototype>().FirstOrDefault(p => p.World.Id == world);
    }

    /// <summary>A random site nobody holds or lives on, as expeditions take them.</summary>
    private int? FreeSite(WFPlanetPoiComponent record)
    {
        var free = Enumerable.Range(0, record.Sites.Count)
            .Where(i => record.Sites[i] is { Expeditions: true, ExpeditionClaim: null, ContractClaim: null, Cleared: false, City: false })
            .ToList();
        return free.Count == 0 ? null : _random.Pick(free);
    }

    /// <summary>Picks the world a planetside offer is posted for.</summary>
    private bool RollPlanet(WFContract contract, WFContractPlanet planet)
    {
        var grounds = PlanetGrounds(planet);
        if (grounds.Count == 0)
            return false;

        var (ground, world) = _random.Pick(grounds);
        contract.PlanetGround = ground;
        contract.PlanetWorld = world;
        return true;
    }

    /// <summary>Claims a free site on the contract's world and arms it with natives; the ground's grid, or null.</summary>
    private Entity<MapGridComponent>? ClaimSite(WFContract contract, WFContractPlanet planet, int tier)
    {
        if (HeldSite(contract) is not { } index)
        {
            // The world picked at posting may have filled up since; another allowed one will do.
            if (contract.PlanetGround is not { } picked || !TryComp<WFPlanetPoiComponent>(picked, out var pickedRecord)
                || FreeSite(pickedRecord) == null)
            {
                if (!RollPlanet(contract, planet))
                    return null;
            }

            if (FreeSite(Comp<WFPlanetPoiComponent>(contract.PlanetGround!.Value)) is not { } free)
                return null;
            index = free;
        }

        var ground = contract.PlanetGround!.Value;
        var record = Comp<WFPlanetPoiComponent>(ground);
        if (ExcludedWorlds.Contains(contract.PlanetWorld ?? string.Empty) || !TryComp<MapGridComponent>(ground, out var grid))
            return null;

        var site = record.Sites[index];
        site.ContractClaim = contract.Home;
        contract.PlanetSite = index;
        contract.SiteBounds = site.Bounds;
        var centre = (site.Bounds.BottomLeft + site.Bounds.TopRight) / 2;
        contract.Site = _transform.ToMapCoordinates(_map.GridTileToLocal(ground, grid, centre));

        if (TryComp<WFPoiSignalComponent>(site.Signal, out var signal) && !signal.Revealed)
            _pois.Reveal((site.Signal, signal), null);

        SpawnNatives(contract, (ground, grid), planet.Hostiles, tier);
        return (ground, grid);
    }

    /// <summary>The site held for a chain link, if it is still its own to take.</summary>
    private int? HeldSite(WFContract contract)
    {
        if (contract.PreferredSite is not { } index || contract.PlanetGround is not { } ground
            || !TryComp<WFPlanetPoiComponent>(ground, out var record) || index >= record.Sites.Count)
            return null;

        var site = record.Sites[index];
        return site.ExpeditionClaim == null && (site.ContractClaim == null || site.ContractClaim == contract.Home) ? index : null;
    }

    /// <summary>Holds a finished link's site for the next link of its chain.</summary>
    private void HoldSite(WFContract offer, EntityUid ground, string? world, int index)
    {
        if (world == null || ExcludedWorlds.Contains(world) || !TryComp<WFPlanetPoiComponent>(ground, out var record)
            || index >= record.Sites.Count || record.Sites[index].ExpeditionClaim != null
            || record.Sites[index].ContractClaim is { } other && other != offer.Home)
            return;

        offer.PlanetGround = ground;
        offer.PlanetWorld = world;
        offer.PreferredSite = index;
        record.Sites[index].ContractClaim = offer.Home;
    }

    /// <summary>Gives back a site held for a chain link nobody took.</summary>
    private void ReleaseHeld(WFContract offer)
    {
        if (offer.PreferredSite is { } index && offer.PlanetSite == null && offer.PlanetGround is { } ground
            && TryComp<WFPlanetPoiComponent>(ground, out var record) && index < record.Sites.Count
            && record.Sites[index].ContractClaim == offer.Home)
            record.Sites[index].ContractClaim = null;
    }

    /// <summary>Places native hostiles on the site: mostly common ones, tougher ones from tier 2, one boss from tier 4.</summary>
    private void SpawnNatives(WFContract contract, Entity<MapGridComponent> grid, Vector2i range, int tier)
    {
        if (contract.PlanetWorld is not { } world || FaunaFor(world) is not { } fauna)
            return;

        var count = _random.Next(range.X, Math.Max(range.X, range.Y) + 1);
        var heavyChance = tier switch { <= 1 => 0f, 2 => 0.3f, 3 => 0.6f, _ => 0.75f };
        for (var i = 0; i < count; i++)
        {
            var pool = fauna.Heavy.Count > 0 && _random.Prob(heavyChance) ? fauna.Heavy : fauna.Light;
            PlaceNative(contract, grid, _random.Pick(pool));
        }

        if (tier >= 4 && fauna.Boss.Count > 0)
            PlaceNative(contract, grid, _random.Pick(fauna.Boss));
    }

    private void PlaceNative(WFContract contract, Entity<MapGridComponent> grid, string proto)
    {
        if (FreeTile(grid, contract.SiteBounds) is not { } tile)
            return;

        var uid = Spawn(proto, tile);
        // Contract hostiles are not ghost roles.
        RemComp<GhostTakeoverAvailableComponent>(uid);
        RemComp<GhostRoleComponent>(uid);
        contract.Spawned.Add(uid);
    }

    /// <summary>Gives a planet site back; what was left on it goes unless someone is there to see it.</summary>
    private void ReleaseSite(WFContract contract)
    {
        if (contract.PlanetGround is not { } ground || contract.PlanetSite is not { } index)
            return;

        if (TryComp<WFPlanetPoiComponent>(ground, out var record) && index < record.Sites.Count
            && record.Sites[index].ContractClaim == contract.Home)
            record.Sites[index].ContractClaim = null;

        var watched = contract.Site is { } site && PlayerNearPoint(site, SiteKeepRange);
        foreach (var uid in contract.Spawned.Concat(contract.Targets).Concat(contract.Anomalies))
        {
            if (!watched && !TerminatingOrDeleted(uid))
                QueueDel(uid);
        }

        contract.Spawned.Clear();
        contract.Targets.Clear();
        contract.Anomalies.Clear();
        contract.PlanetSite = null;
    }

    /// <summary>The grid the contract's objective stands on: its wreck, or its world's ground while it holds a site.</summary>
    private Entity<MapGridComponent>? SiteGrid(WFContract contract)
    {
        var uid = contract.WreckGrid ?? (contract.PlanetSite != null ? contract.PlanetGround : null);
        return uid is { } grid && !TerminatingOrDeleted(grid) && TryComp<MapGridComponent>(grid, out var comp) ? (grid, comp) : null;
    }

    /// <summary>Whether an entity has left its contract's wreck or planet site.</summary>
    private bool OffSite(WFContract contract, EntityUid uid)
    {
        var xform = Transform(uid);
        if (contract.WreckGrid is { } wreck)
            return xform.GridUid != wreck;

        if (contract.PlanetGround is not { } ground || contract.SiteBounds is not { } bounds || xform.GridUid != ground)
            return true;

        var tile = _map.TileIndicesFor(ground, Comp<MapGridComponent>(ground), xform.Coordinates);
        return !bounds.Contains(tile);
    }

    /// <summary>Whether a kit stands free on the ground within reach of its contract's site.</summary>
    private bool SetDownOnSite(WFContract contract, EntityUid kit)
    {
        if (contract.PlanetGround is not { } ground || contract.SiteBounds is not { } bounds)
            return false;

        var xform = Transform(kit);
        if (xform.GridUid != ground || xform.ParentUid != ground)
            return false;

        var tile = _map.TileIndicesFor(ground, Comp<MapGridComponent>(ground), xform.Coordinates);
        var reach = new Box2i(bounds.BottomLeft - new Vector2i(KitMargin, KitMargin), bounds.TopRight + new Vector2i(KitMargin, KitMargin));
        return reach.Contains(tile);
    }

    /// <summary>The site's name and world, for the board and app.</summary>
    private (string Site, string World) PlanetNames(WFContract contract)
    {
        var world = contract.PlanetGround is { } ground && !TerminatingOrDeleted(ground) ? Name(ground) : Loc.GetString("wf-contract-world-unknown");
        var site = contract.PlanetGround is { } g && contract.PlanetSite is { } i && TryComp<WFPlanetPoiComponent>(g, out var record)
                   && i < record.Sites.Count
            ? Loc.GetString(record.Sites[i].Name)
            : Loc.GetString("wf-contract-site-unknown");
        return (site, world);
    }

    private bool PlayerNearPoint(MapCoordinates point, float range)
    {
        var players = EntityQueryEnumerator<Robust.Shared.Player.ActorComponent, TransformComponent>();
        while (players.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapID == point.MapId && (_transform.GetWorldPosition(xform) - point.Position).Length() <= range)
                return true;
        }

        return false;
    }

    /// <summary>Whether a target is out of the fight: gone, or a creature lying dead.</summary>
    private bool Cleared(EntityUid target)
    {
        return TerminatingOrDeleted(target) || TryComp<MobStateComponent>(target, out var state) && _mobState.IsDead(target, state);
    }
}
