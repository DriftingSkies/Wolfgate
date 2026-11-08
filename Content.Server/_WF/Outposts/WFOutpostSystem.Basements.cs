using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._Mono.Cleanup;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Caverns;
using Content.Server._WF.Outposts.Components;
using Content.Shared._WF.Outposts;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Basements: an outpost is a set of grids, one per depth, like a multi-deck ship. A hatch kit used on its floor digs a
/// room into the cavern below on the outpost's depth -1 grid, opens the floor over it and sets a CE ladder under the hole,
/// with a connector pillar binding the two grids. Foundation plates dig the basement further, within the outpost's claim;
/// floor caps count both grids. Saves carry each grid under its depth.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private CEZLevelsSystem _zNetworks = default!;

    /// <summary>Key in a save's YAML holding the grids below the outpost, each with its depth.</summary>
    public const string DepthsKey = "wfDepths";

    /// <summary>The floor laid back over a hatch whose basement is gone.</summary>
    private const string HatchFill = "Plating";

    /// <summary>Cavern to put back under basements deleted this tick, by cavern map.</summary>
    private readonly List<(EntityUid Map, Dictionary<Vector2i, Tile> Carved)> _cavernRestores = new();

    private static readonly Vector2i[] Around =
    {
        new(-1, -1), new(0, -1), new(1, -1), new(-1, 0), new(1, 0), new(-1, 1), new(0, 1), new(1, 1),
    };

    private void InitializeBasements()
    {
        SubscribeLocalEvent<WFOutpostHatchKitComponent, AfterInteractEvent>(OnHatchKitAfterInteract);
        SubscribeLocalEvent<WFOutpostComponent, EntityTerminatingEvent>(OnOutpostTerminating);
        SubscribeLocalEvent<WFOutpostBasementComponent, EntityTerminatingEvent>(OnBasementTerminating);
    }

    // Each grid leaves its z-network as it goes, or CE pins it back onto the other while it is detached for deletion.
    private void OnOutpostTerminating(Entity<WFOutpostComponent> outpost, ref EntityTerminatingEvent args)
    {
        _zNetworks.TryRemoveGridFromNetwork(outpost);
    }

    /// <summary>
    /// Unlinks a basement that is going and notes the cavern to put back under it, while it still stands on the cavern;
    /// the cavern goes back once the basement is gone.
    /// </summary>
    private void OnBasementTerminating(Entity<WFOutpostBasementComponent> basement, ref EntityTerminatingEvent args)
    {
        _zNetworks.TryRemoveGridFromNetwork(basement);
        if (TryComp<WFOutpostComponent>(basement.Comp.Outpost, out var outpost) && outpost.Basement == basement.Owner)
            outpost.Basement = null;

        if (basement.Comp.Carved.Count > 0 && Transform(basement).MapUid is { } map && !TerminatingOrDeleted(map))
            _cavernRestores.Add((map, new Dictionary<Vector2i, Tile>(basement.Comp.Carved)));
    }

    /// <summary>Puts back the cavern under basements that have gone.</summary>
    private void RestoreCaverns()
    {
        foreach (var (map, carved) in _cavernRestores)
        {
            if (!TerminatingOrDeleted(map) && TryComp<MapGridComponent>(map, out var cavern))
                _biome.WfRestoreCarved((map, cavern), carved);
        }

        _cavernRestores.Clear();
    }

    private void OnHatchKitAfterInteract(Entity<WFOutpostHatchKitComponent> kit, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        args.Handled = true;
        if (!TryDigHatch(args.User, kit, args.ClickLocation, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
            return;
        }

        _popup.PopupEntity(Loc.GetString("wf-outpost-hatch-done"), args.User, args.User);
        QueueDel(kit);
    }

    /// <summary>The live basement grid under an outpost, if it has one.</summary>
    public bool TryGetBasement(WFOutpostComponent outpost, out Entity<WFOutpostBasementComponent, MapGridComponent> basement)
    {
        if (outpost.Basement is { } uid && !TerminatingOrDeleted(uid)
            && TryComp<WFOutpostBasementComponent>(uid, out var comp) && TryComp<MapGridComponent>(uid, out var grid))
        {
            basement = (uid, comp, grid);
            return true;
        }

        basement = default;
        return false;
    }

    /// <summary>Everything on every depth of an outpost: what the entity cap counts.</summary>
    public int CountAllEntities(Entity<WFOutpostComponent> outpost)
    {
        var count = CountEntities(outpost);
        if (TryGetBasement(outpost.Comp, out var basement))
            count += CountEntities(basement);
        return count;
    }

    /// <summary>Floor tiles across every depth of an outpost: what the floor cap counts.</summary>
    public int CountAllTiles(Entity<WFOutpostComponent, MapGridComponent> outpost)
    {
        var tiles = CountTiles((outpost, outpost.Comp2));
        if (TryGetBasement(outpost.Comp1, out var basement))
            tiles += CountTiles((basement, basement.Comp2));
        return tiles;
    }

    /// <summary>
    /// Opens a hatch in an outpost's floor at <paramref name="target"/>: digs a room round it on the outpost's basement,
    /// founding the basement if it has none, takes the floor tile away and sets a ladder under it. The tile has to be
    /// floor all round, with nothing standing on it. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryDigHatch(EntityUid user, Entity<WFOutpostHatchKitComponent> kit, EntityCoordinates target, out string reason)
    {
        if (_transform.GetGrid(target) is not { } gridUid || !TryComp<WFOutpostComponent>(gridUid, out var outpostComp)
            || !TryComp<MapGridComponent>(gridUid, out var grid) || !IsLanded(gridUid, outpostComp))
        {
            reason = "wf-outpost-hatch-not-outpost";
            return false;
        }

        Entity<WFOutpostComponent, MapGridComponent> outpost = (gridUid, outpostComp, grid);
        if (!CanBuild(user, (gridUid, outpostComp)) || HasComp<WFOutpostPendingLoadComponent>(gridUid))
        {
            reason = "wf-outpost-hatch-not-builder";
            return false;
        }

        if (!TryGetCavern(outpostComp, out var cavern))
        {
            reason = "wf-outpost-hatch-no-cavern";
            return false;
        }

        var index = _maps.TileIndicesFor(gridUid, grid, target);
        if (!HasFloor((gridUid, grid), index))
        {
            reason = "wf-outpost-hatch-no-floor";
            return false;
        }

        foreach (var offset in Around)
        {
            if (HasFloor((gridUid, grid), index + offset))
                continue;

            reason = "wf-outpost-hatch-edge";
            return false;
        }

        var anchored = _maps.GetAnchoredEntitiesEnumerator(gridUid, grid, index);
        if (anchored.MoveNext(out _))
        {
            reason = "wf-outpost-hatch-blocked";
            return false;
        }

        var room = new List<Vector2i>();
        for (var x = -kit.Comp.Reach; x <= kit.Comp.Reach; x++)
        for (var y = -kit.Comp.Reach; y <= kit.Comp.Reach; y++)
            room.Add(index + new Vector2i(x, y));

        TryGetBasement(outpostComp, out var existing);
        var fresh = 0;
        foreach (var tile in room)
        {
            if (existing.Owner == default || !HasFloor((existing.Owner, existing.Comp2), tile))
                fresh++;
            else if (tile == index && HasAnchored((existing.Owner, existing.Comp2), tile))
            {
                reason = "wf-outpost-hatch-blocked-below";
                return false;
            }

            if (!IsUnderClaim(outpost, cavern, tile))
            {
                reason = "wf-outpost-hatch-claim";
                return false;
            }
        }

        if (CountAllTiles(outpost) + fresh - 1 > _cfg.GetCVar(OutpostCVars.MaxTiles))
        {
            reason = "wf-outpost-plate-full";
            return false;
        }

        var basement = existing.Owner != default ? existing : CreateBasement(outpost, cavern);
        Dig(basement, cavern, room, kit.Comp.Tile);
        CarveUnder(outpost, new List<Vector2i> { index });

        _maps.SetTile(gridUid, grid, index, Tile.Empty);
        outpostComp.Hatches.Add(index);
        SpawnAnchored(kit.Comp.Ladder, (basement.Owner, basement.Comp2), index);
        // The pillar stands where the outpost above has floor, out of the way of the ladder's foot.
        SpawnAnchored(kit.Comp.Connector, (basement.Owner, basement.Comp2), index + new Vector2i(kit.Comp.Reach, kit.Comp.Reach));
        RebuildPortals(outpost);

        _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
            $"{ToPrettyString(user):user} opened a basement hatch on outpost {ToPrettyString(gridUid):grid} at {index}");
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Digs an outpost's basement one tile further, at <paramref name="target"/> on the cavern beside its floor, for a
    /// foundation plate. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    private bool TryExtendBasement(EntityUid user, string tileId, EntityCoordinates target, out string reason)
    {
        var map = _transform.GetMap(target);
        var at = _transform.ToMapCoordinates(target);
        var query = EntityQueryEnumerator<WFOutpostBasementComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var basement, out var grid))
        {
            if (Transform(uid).MapUid != map || !TryComp<WFOutpostComponent>(basement.Outpost, out var outpostComp)
                || !TryComp<MapGridComponent>(basement.Outpost, out var outpostGrid) || !CanBuild(user, (basement.Outpost, outpostComp)))
                continue;

            var index = _maps.TileIndicesFor(uid, grid, at);
            if (HasFloor((uid, grid), index))
            {
                reason = "wf-outpost-plate-taken";
                return false;
            }

            var beside = false;
            foreach (var side in Neighbours)
            {
                if (HasFloor((uid, grid), index + side))
                    beside = true;
            }

            if (!beside)
                continue;

            Entity<WFOutpostComponent, MapGridComponent> outpost = (basement.Outpost, outpostComp, outpostGrid);
            if (!TryGetCavern(outpostComp, out var cavern) || !IsUnderClaim(outpost, cavern, index))
            {
                reason = "wf-outpost-hatch-claim";
                return false;
            }

            if (CountAllTiles(outpost) >= _cfg.GetCVar(OutpostCVars.MaxTiles))
            {
                reason = "wf-outpost-plate-full";
                return false;
            }

            Dig((uid, basement, grid), cavern, new List<Vector2i> { index }, tileId);
            _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
                $"{ToPrettyString(user):user} dug the basement of outpost {ToPrettyString(basement.Outpost):grid} out to {index}");
            reason = string.Empty;
            return true;
        }

        reason = "wf-outpost-plate-not-outpost";
        return false;
    }

    /// <summary>The cavern map under an outpost's ground, if its world has one.</summary>
    private bool TryGetCavern(WFOutpostComponent outpost, out Entity<MapGridComponent> cavern)
    {
        if (TryComp<WFCavernGroundComponent>(outpost.Ground, out var ground) && !TerminatingOrDeleted(ground.Cavern)
            && TryComp<MapGridComponent>(ground.Cavern, out var grid))
        {
            cavern = (ground.Cavern, grid);
            return true;
        }

        cavern = default;
        return false;
    }

    /// <summary>Whether a tile of the outpost's grids lies over the cavern its claim holds as foundation.</summary>
    private bool IsUnderClaim(Entity<WFOutpostComponent, MapGridComponent> outpost, Entity<MapGridComponent> cavern, Vector2i index)
    {
        var (min, max) = GetClaimZone(outpost);
        var world = _transform.ToMapCoordinates(new EntityCoordinates(outpost, index + new Vector2(0.5f)));
        var under = _maps.TileIndicesFor(cavern, cavern.Comp, new MapCoordinates(world.Position, Transform(cavern).MapID));
        return under.X >= min.X && under.Y >= min.Y && under.X <= max.X && under.Y <= max.Y;
    }

    /// <summary>A new, empty basement grid on the cavern, lined up with the outpost above it.</summary>
    private Entity<WFOutpostBasementComponent, MapGridComponent> CreateBasement(Entity<WFOutpostComponent, MapGridComponent> outpost,
        Entity<MapGridComponent> cavern)
    {
        var grid = _maps.CreateGridEntity(Transform(cavern).MapID);
        _metaData.SetEntityName(grid, Loc.GetString("wf-outpost-basement-name", ("outpost", Name(outpost))));
        var comp = EnsureComp<WFOutpostBasementComponent>(grid);
        PlaceBasement(outpost, (grid, comp, grid.Comp), cavern);
        return (grid, comp, grid.Comp);
    }

    /// <summary>Puts a basement grid on the cavern under its outpost, held down and linked both ways.</summary>
    private void PlaceBasement(Entity<WFOutpostComponent, MapGridComponent> outpost, Entity<WFOutpostBasementComponent, MapGridComponent> basement,
        Entity<MapGridComponent> cavern)
    {
        _transform.SetCoordinates(basement, new EntityCoordinates(cavern, Vector2.Zero));
        _transform.SetWorldPositionRotation(basement, _transform.GetWorldPosition(outpost), _transform.GetWorldRotation(outpost));
        EnsureComp<ForceAnchorComponent>(basement);
        EnsureComp<PreventGridAnchorChangesComponent>(basement);
        _shuttle.Disable(basement, force: true);
        EnsureComp<CleanupImmuneComponent>(basement);
        basement.Comp1.Outpost = outpost;
        outpost.Comp1.Basement = basement;
    }

    /// <summary>Floors the given basement tiles, digging the cavern out from under them.</summary>
    private void Dig(Entity<WFOutpostBasementComponent, MapGridComponent> basement, Entity<MapGridComponent> cavern, List<Vector2i> tiles,
        string tileId)
    {
        var floor = new Tile(_tiles[tileId].TileId);
        var laid = new List<(Vector2i, Tile)>();
        foreach (var tile in tiles)
        {
            if (!HasFloor((basement, basement.Comp2), tile))
                laid.Add((tile, floor));
        }

        _maps.SetTiles(basement, basement.Comp2, laid);
        CarveCavern(basement, cavern, tiles);
    }

    /// <summary>Takes the cavern's own tiles and rock out from under basement tiles.</summary>
    private void CarveCavern(Entity<WFOutpostBasementComponent, MapGridComponent> basement, Entity<MapGridComponent> cavern, List<Vector2i> tiles)
    {
        var indices = new List<Vector2i>();
        foreach (var tile in tiles)
        {
            var world = _transform.ToMapCoordinates(new EntityCoordinates(basement, tile + new Vector2(0.5f)));
            indices.Add(_maps.TileIndicesFor(cavern, cavern.Comp, world));
        }

        _biome.WfCarve(cavern, indices, basement.Comp1.Carved);
    }

    /// <summary>
    /// Brings a staged basement down under an outpost that has just settled. Without a cavern there, the basement is
    /// dropped and its hatches floored over.
    /// </summary>
    private void SettleBasement(Entity<WFOutpostComponent, MapGridComponent> outpost)
    {
        if (!TryGetBasement(outpost.Comp1, out var basement))
        {
            CloseHatches(outpost);
            return;
        }

        if (!TryGetCavern(outpost.Comp1, out var cavern))
        {
            outpost.Comp1.Basement = null;
            Del(basement);
            CloseHatches(outpost);
            return;
        }

        PlaceBasement(outpost, basement, cavern);
        var all = new List<Vector2i>();
        var tiles = _maps.GetAllTilesEnumerator(basement, basement.Comp2);
        while (tiles.MoveNext(out var tile))
        {
            all.Add(tile.Value.GridIndices);
        }

        CarveCavern(basement, cavern, all);
        // The hatches are holes in the outpost's floor, which the outpost's own carving passes over.
        CarveUnder(outpost, new List<Vector2i>(outpost.Comp1.Hatches));
        _reinit.ReinitLoadedShip(basement);
    }

    /// <summary>Floors over an outpost's hatches, for when there is no basement under them.</summary>
    private void CloseHatches(Entity<WFOutpostComponent, MapGridComponent> outpost)
    {
        if (outpost.Comp1.Hatches.Count == 0 || TerminatingOrDeleted(outpost))
            return;

        var floor = new Tile(_tiles[HatchFill].TileId);
        var fill = new List<(Vector2i, Tile)>();
        foreach (var hatch in outpost.Comp1.Hatches)
        {
            if (!HasFloor((outpost, outpost.Comp2), hatch))
                fill.Add((hatch, floor));
        }

        outpost.Comp1.Hatches.Clear();
        _maps.SetTiles(outpost, outpost.Comp2, fill);
    }

    /// <summary>Takes an outpost's basement away with it: whoever and whatever lies loose down there is brought up to the ground.</summary>
    private void RemoveBasement(Entity<WFOutpostComponent> outpost)
    {
        if (!TryGetBasement(outpost.Comp, out var basement))
            return;

        outpost.Comp.Basement = null;
        if (!TerminatingOrDeleted(outpost.Comp.Ground))
        {
            var loose = new List<EntityUid>();
            var children = Transform(basement).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (!Transform(child).Anchored)
                    loose.Add(child);
            }

            foreach (var ent in loose)
            {
                _transform.SetCoordinates(ent, new EntityCoordinates(outpost.Comp.Ground, _transform.GetWorldPosition(ent)));
            }
        }

        Del(basement);
        if (!TerminatingOrDeleted(outpost) && TryComp<MapGridComponent>(outpost, out var grid))
            CloseHatches((outpost, outpost.Comp, grid));
    }

    /// <summary>Serialises an outpost's basement as its own grid and files it in the save under its depth.</summary>
    private void SerializeDepths(Entity<WFOutpostComponent> outpost, MappingDataNode node, SerializationOptions options)
    {
        if (!TryGetBasement(outpost.Comp, out var basement))
            return;

        Stamp(basement);
        var (grid, category) = _mapLoader.SerializeEntitiesRecursive([basement.Owner], options);
        if (category != FileCategory.Grid)
            throw new InvalidOperationException($"Basement saved as {category}, not as a grid.");
        DropRecordKeys(grid);

        var depth = new MappingDataNode();
        depth.Add("depth", new ValueDataNode(basement.Comp1.Depth.ToString()));
        depth.Add("grid", grid);
        node.Add(DepthsKey, new SequenceDataNode(new List<DataNode> { depth }));
    }

    /// <summary>Takes the grids below the outpost out of a save's YAML, by depth, leaving the outpost's own grid.</summary>
    public static List<(int Depth, MappingDataNode Grid)> TakeDepths(MappingDataNode node)
    {
        var depths = new List<(int, MappingDataNode)>();
        if (!node.TryGet<SequenceDataNode>(DepthsKey, out var list))
            return depths;

        node.Remove(DepthsKey);
        foreach (var entry in list)
        {
            if (entry is MappingDataNode mapping && mapping.TryGet<ValueDataNode>("depth", out var depth)
                && int.TryParse(depth.Value, out var value) && mapping.TryGet<MappingDataNode>("grid", out var grid))
                depths.Add((value, grid));
        }

        return depths;
    }

    private bool HasFloor(Entity<MapGridComponent> grid, Vector2i index)
    {
        return _maps.TryGetTileRef(grid, grid.Comp, index, out var tile) && !tile.Tile.IsEmpty;
    }

    private bool HasAnchored(Entity<MapGridComponent> grid, Vector2i index)
    {
        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, grid.Comp, index);
        return anchored.MoveNext(out _);
    }

    private void SpawnAnchored(EntProtoId proto, Entity<MapGridComponent> grid, Vector2i index)
    {
        var uid = Spawn(proto, _maps.GridTileToLocal(grid, grid.Comp, index));
        var xform = Transform(uid);
        if (!xform.Anchored && !_transform.AnchorEntity((uid, xform), grid, index))
            Log.Error($"Could not anchor {ToPrettyString(uid)} on {ToPrettyString(grid)} at {index}.");
    }
}
