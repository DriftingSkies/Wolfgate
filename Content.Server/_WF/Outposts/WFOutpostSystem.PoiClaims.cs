using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server.NPC.HTN;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Claiming a point of interest: its terminal turns the site into an outpost of the claimant's, on its own grid, worth
/// nothing at sale but what is added to it.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;

    /// <summary>The share of a world's claimable sites that may be claimed at once.</summary>
    public const float ClaimableShare = 0.5f;

    /// <summary>
    /// Claims the site of <paramref name="terminal"/> for <paramref name="user"/> once nothing hostile is left in it and
    /// its terminal is repaired or hacked as its type asks. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryClaimSite(EntityUid user, Entity<WFPoiClaimTerminalComponent> terminal,
        [NotNullWhen(true)] out EntityUid? outpost, out string reason)
    {
        outpost = null;
        if (!_cfg.GetCVar(OutpostCVars.Enabled))
        {
            reason = "wf-outpost-found-disabled";
            return false;
        }

        if (terminal.Comp.Site is not { } site || !TryComp<WFPlanetPoiComponent>(terminal.Comp.Ground, out var pois)
            || site.Type is not { } typeId || !_proto.TryIndex(typeId, out var type) || type.Claim == WFPoiClaim.None)
        {
            reason = "wf-poi-claim-not-claimable";
            return false;
        }

        if (site.ClaimedBy is { } claimed && !TerminatingOrDeleted(claimed))
        {
            reason = "wf-poi-claim-taken";
            return false;
        }

        if (!TryComp<ActorComponent>(user, out var actor))
        {
            reason = "wf-outpost-found-no-owner";
            return false;
        }

        var owner = actor.PlayerSession.UserId;
        if (TryGetOutpost(owner, out _))
        {
            reason = "wf-outpost-found-already-own";
            return false;
        }

        switch (type.Claim)
        {
            case WFPoiClaim.Repair when CompOrNull<DamageableComponent>(terminal)?.TotalDamage > 0:
                reason = "wf-poi-claim-needs-repair";
                return false;
            case WFPoiClaim.Hack when !terminal.Comp.Hacked:
                reason = "wf-poi-claim-needs-hack";
                return false;
        }

        var claimable = pois.Sites.Count(s => s.Type is { } t && _proto.TryIndex(t, out var kind) && kind.Claim != WFPoiClaim.None);
        var taken = pois.Sites.Count(s => s.ClaimedBy is { } by && !TerminatingOrDeleted(by));
        if (taken >= Math.Max(1, (int) (claimable * ClaimableShare)))
        {
            reason = "wf-poi-claim-cap";
            return false;
        }

        var siteGrid = SiteGrid(terminal.Comp.Ground, site);
        if (HasHostiles(user, terminal.Comp.Ground, siteGrid, site))
        {
            reason = "wf-poi-claim-hostiles";
            return false;
        }

        var grid = siteGrid ?? SplitSite(terminal.Comp.Ground, site);
        if (grid is not { } outpostGrid)
        {
            reason = "wf-poi-claim-not-claimable";
            return false;
        }

        // A wreck stops being the world's.
        RemComp<WFPlanetCityGridComponent>(outpostGrid);
        HoldDown(outpostGrid);
        var gridComp = Comp<MapGridComponent>(outpostGrid);
        var ownerName = MetaData(user).EntityName;
        var comp = EnsureComp<WFOutpostComponent>(outpostGrid);
        comp.Owner = owner;
        comp.OwnerName = ownerName;
        comp.Ground = terminal.Comp.Ground;
        comp.Lineage = Guid.NewGuid().ToString();
        _metaData.SetEntityName(outpostGrid, Loc.GetString(site.Name));

        // The terminal makes way for the outpost's console.
        var at = Transform(terminal).Coordinates;
        var console = Spawn("WFOutpostConsole", at);
        EnsureComp<CleanupImmuneComponent>(console);
        EnsureComp<WFOutpostConsoleComponent>(console).Outpost = outpostGrid;
        comp.Console = console;
        Del(terminal);

        var all = new List<Vector2i>();
        var tiles = _maps.GetAllTilesEnumerator(outpostGrid, gridComp);
        while (tiles.MoveNext(out var tile))
        {
            all.Add(tile.Value.GridIndices);
        }

        comp.FoundingTiles = all.Count;
        CarveUnder((outpostGrid, comp, gridComp), all);
        RebuildPortals((outpostGrid, comp, gridComp));
        SetupAccess((outpostGrid, comp), user);
        comp.ClaimBaseline = Value((outpostGrid, gridComp), IsSold);
        site.ClaimedBy = outpostGrid;
        site.Terminal = null;

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{ToPrettyString(user):user} claimed {site.Name} as outpost {ToPrettyString(outpostGrid):grid}, baseline {comp.ClaimBaseline}");
        outpost = outpostGrid;
        reason = string.Empty;
        return true;
    }

    /// <summary>The grid a site stands on of its own, such as a wreck's hull, or null for a site built into the ground.</summary>
    public EntityUid? SiteGrid(EntityUid ground, WFPlanetPoiSite site)
    {
        var box = new Box2(site.Bounds.Left, site.Bounds.Bottom, site.Bounds.Right, site.Bounds.Top);
        _grids.Clear();
        _maps.FindGridsIntersecting(Transform(ground).MapID, _transform.GetWorldMatrix(ground).TransformBox(box), ref _grids,
            includeMap: false);
        foreach (var grid in _grids)
        {
            if (grid.Owner != ground && HasComp<WFPlanetCityGridComponent>(grid) && !HasComp<WFOutpostComponent>(grid))
                return grid.Owner;
        }

        return null;
    }

    /// <summary>
    /// The ground tiles of a site built into the ground: within its bounds and not the world's own, as laid floor or with
    /// something built on them. Roads and footpaths are left to the world.
    /// </summary>
    public List<Vector2i> SiteTiles(Entity<MapGridComponent> ground, WFPlanetPoiSite site)
    {
        var result = new List<Vector2i>();
        var pois = CompOrNull<WFPlanetPoiComponent>(ground);
        var biome = CompOrNull<BiomeComponent>(ground);
        for (var x = site.Bounds.Left; x < site.Bounds.Right; x++)
        for (var y = site.Bounds.Bottom; y < site.Bounds.Top; y++)
        {
            var index = new Vector2i(x, y);
            if (pois != null && (pois.RoadTiles.Contains(index) || pois.ShoulderTiles.Contains(index) || pois.TrailTiles.Contains(index)))
                continue;

            if (!_maps.TryGetTileRef(ground, ground.Comp, index, out var tile) || tile.Tile.IsEmpty)
                continue;

            // The world's own tile there, whatever is laid on it now.
            var natural = biome != null && _biome.TryGetTile(index, biome.Layers, biome.Seed, null, out var biomeTile)
                && biomeTile.Value.TypeId == tile.Tile.TypeId;
            if (!natural || HasBuilt(ground, index))
                result.Add(index);
        }

        return result;
    }

    private bool HasBuilt(Entity<MapGridComponent> ground, Vector2i index)
    {
        var anchored = _maps.GetAnchoredEntitiesEnumerator(ground, ground.Comp, index);
        while (anchored.MoveNext(out var ent))
        {
            if (!_biome.WfIsBiomeAnchored(ground, ent.Value, index))
                return true;
        }

        return false;
    }

    /// <summary>Lifts a site built into the ground onto a grid of its own: its floor, roof and everything anchored on it.</summary>
    private EntityUid? SplitSite(EntityUid ground, WFPlanetPoiSite site)
    {
        if (!TryComp<MapGridComponent>(ground, out var groundGrid))
            return null;

        var indices = SiteTiles((ground, groundGrid), site);
        if (indices.Count == 0)
            return null;

        var grid = _maps.CreateGridEntity(Transform(ground).MapID);
        _transform.SetWorldPositionRotation(grid.Owner, Vector2.Transform(Vector2.Zero, _transform.GetWorldMatrix(ground)),
            _transform.GetWorldRotation(ground));
        _maps.SetTiles(grid.Owner, grid.Comp,
            indices.Select(index => (index, _maps.GetTileRef(ground, groundGrid, index).Tile)).ToList());

        var groundRoof = CompOrNull<RoofComponent>(ground);
        var roof = EnsureComp<RoofComponent>(grid);
        var moving = new List<EntityUid>();
        foreach (var index in indices)
        {
            if (groundRoof != null && _roof.IsRooved((ground, groundGrid, groundRoof), index))
                _roof.SetRoof((grid.Owner, grid.Comp, roof), index, true);

            var anchored = _maps.GetAnchoredEntitiesEnumerator(ground, groundGrid, index);
            while (anchored.MoveNext(out var ent))
            {
                if (!_biome.WfIsBiomeAnchored(ground, ent.Value, index))
                    moving.Add(ent.Value);
            }
        }

        foreach (var ent in moving)
        {
            var xform = Transform(ent);
            _transform.SetCoordinates(ent, xform, new EntityCoordinates(grid, xform.LocalPosition), xform.LocalRotation);
            if (!xform.Anchored)
                _transform.AnchorEntity((ent, xform), (grid.Owner, grid.Comp));
        }

        return grid;
    }

    /// <summary>Whether anything alive in the site would fight the claimant.</summary>
    private bool HasHostiles(EntityUid user, EntityUid ground, EntityUid? siteGrid, WFPlanetPoiSite site)
    {
        var box = new Box2(site.Bounds.Left, site.Bounds.Bottom, site.Bounds.Right, site.Bounds.Top);
        var worldBox = _transform.GetWorldMatrix(ground).TransformBox(box);
        var userPos = _transform.GetWorldPosition(user);
        foreach (var mob in _lookup.GetEntitiesIntersecting(Transform(ground).MapID, worldBox, LookupFlags.Dynamic))
        {
            if (mob == user || !HasComp<HTNComponent>(mob) || !_mobState.IsAlive(mob)
                || !TryComp<NpcFactionMemberComponent>(mob, out var faction))
                continue;

            // As the mob sees it: the claimant is among its hostiles once in reach.
            var range = (_transform.GetWorldPosition(mob) - userPos).Length() + 1f;
            if (_faction.GetNearbyHostiles((mob, faction, null), range).Contains(user))
                return true;
        }

        return false;
    }
}
