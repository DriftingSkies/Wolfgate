using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Outposts.Components;
using Content.Shared._WF.Outposts;
using Content.Shared.Database;
using Content.Shared.Light.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Prefabs: a grid file laid onto a standing outpost as more of its floor, with its roofs and everything on it. The
/// prefab's bottom middle tile lands on the target and it extends away from the user.
/// </summary>
public sealed partial class WFOutpostSystem
{
    /// <summary>The most floor an outpost may have.</summary>
    public int TileCap(EntityUid outpost)
    {
        return Caps(outpost).Tiles;
    }

    /// <summary>The most a save of an outpost may hold.</summary>
    public int EntityCap(EntityUid outpost)
    {
        return Caps(outpost).Entities;
    }

    /// <summary>The most tiles an outpost's floor may span either way.</summary>
    public int SpanCap(EntityUid outpost)
    {
        return Caps(outpost).Span;
    }

    private WFOutpostCapsEvent Caps(EntityUid outpost)
    {
        var caps = new WFOutpostCapsEvent(_cfg.GetCVar(OutpostCVars.MaxTiles), _cfg.GetCVar(OutpostCVars.MaxEntities),
            _cfg.GetCVar(OutpostCVars.MaxSpan));
        RaiseLocalEvent(outpost, ref caps);
        return caps;
    }

    /// <summary>Marks everything a save keeps on a grid as saved, so it can't also be kept elsewhere.</summary>
    public void StampSaved(EntityUid grid)
    {
        Stamp(grid);
    }

    /// <summary>Takes off a loaded ship what no save keeps; what its strongbox kept stays.</summary>
    public void StripUnkept(EntityUid grid)
    {
        Strip(grid, IsKeptAnywhere);
    }

    /// <summary>
    /// Lays a prefab grid onto the outpost for one of its builders. The tile pointed at has to be in the outpost's claim,
    /// and every tile open ground no one else claims, within the span and the caps. <paramref name="reason"/> is a loc id
    /// when it fails.
    /// </summary>
    public bool TryStampPrefab(EntityUid user, Entity<WFOutpostComponent> outpost, ResPath path, EntityCoordinates target,
        Direction facing, out string reason)
    {
        return TryLayPrefab(user, outpost, path, target, facing, false, out _, out reason);
    }

    /// <summary>
    /// Shows where a prefab would go without laying it: its tiles, outlined on the ground for <paramref name="seconds"/>
    /// the way a save on its way down is. False with a reason, and nothing outlined, where it couldn't go.
    /// </summary>
    public bool TryMarkPrefab(EntityUid user, Entity<WFOutpostComponent> outpost, ResPath path, EntityCoordinates target,
        Direction facing, float seconds, out string reason)
    {
        if (_loads.ContainsKey(outpost))
        {
            reason = "wf-outpost-save-loading";
            return false;
        }

        if (!TryLayPrefab(user, outpost, path, target, facing, true, out var tiles, out reason))
            return false;

        var pending = EnsureComp<WFOutpostPendingLoadComponent>(outpost);
        pending.Tiles = tiles;
        pending.Lands = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(outpost, pending);
        _marks[outpost] = pending.Lands;
        return true;
    }

    /// <summary>Outposts showing a prefab's outline, and when it goes.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _marks = new();

    /// <summary>Takes down prefab outlines whose time is up, unless a save has since started down onto the outpost.</summary>
    private void UpdateMarks()
    {
        if (_marks.Count == 0)
            return;

        foreach (var (outpost, until) in _marks.ToList())
        {
            if (_timing.CurTime < until)
                continue;

            _marks.Remove(outpost);
            if (!_loads.ContainsKey(outpost) && !TerminatingOrDeleted(outpost))
                RemComp<WFOutpostPendingLoadComponent>(outpost);
        }
    }

    private bool TryLayPrefab(EntityUid user, Entity<WFOutpostComponent> outpost, ResPath path, EntityCoordinates target,
        Direction facing, bool markOnly, out List<Vector2i> footprint, out string reason)
    {
        footprint = new List<Vector2i>();
        if (!CanBuild(user, outpost) || !TryComp<MapGridComponent>(outpost, out var gridComp))
        {
            reason = "wf-outpost-prefab-not-builder";
            return false;
        }

        var ground = outpost.Comp.Ground;
        if (_transform.GetMap(target) != ground || !TryComp<MapGridComponent>(ground, out var groundGrid))
        {
            reason = "wf-outpost-plate-wrong-ground";
            return false;
        }

        if (!TryStagePrefab(path, out var source))
        {
            reason = "wf-outpost-prefab-failed";
            return false;
        }

        var turns = Turns(facing, _transform.GetWorldRotation(outpost));
        var tiles = new List<(Vector2i Index, Tile Tile)>();
        var min = new Vector2i(int.MaxValue, int.MaxValue);
        var max = new Vector2i(int.MinValue, int.MinValue);
        var enumerator = _maps.GetAllTilesEnumerator(source, source.Comp);
        while (enumerator.MoveNext(out var tile))
        {
            tiles.Add((tile.Value.GridIndices, tile.Value.Tile));
            min = Vector2i.ComponentMin(min, tile.Value.GridIndices);
            max = Vector2i.ComponentMax(max, tile.Value.GridIndices);
        }

        var anchor = new Vector2i((min.X + max.X) / 2, min.Y);
        var origin = _maps.TileIndicesFor(outpost, gridComp, _transform.ToMapCoordinates(target));
        var placed = tiles.Select(tile => (Index: origin + Turn(tile.Index - anchor, turns), tile.Tile)).ToList();
        if (!IsClearForPrefab((outpost, outpost.Comp, gridComp), (ground, groundGrid), origin, placed.Select(tile => tile.Index), out reason)
            || !FitsCaps((outpost, outpost.Comp, gridComp), placed.Count, CountEntities(source), out reason))
        {
            Del(source);
            return false;
        }

        if (markOnly)
        {
            footprint = placed.Select(tile => tile.Index).ToList();
            Del(source);
            reason = string.Empty;
            return true;
        }

        _maps.SetTiles(outpost, gridComp, placed);
        if (TryComp<RoofComponent>(source, out var sourceRoof) && TryComp<RoofComponent>(outpost, out var roof))
        {
            foreach (var (index, _) in tiles)
            {
                if (_roof.IsRooved((source.Owner, source.Comp, sourceRoof), index))
                    _roof.SetRoof((outpost.Owner, gridComp, roof), origin + Turn(index - anchor, turns), true);
            }
        }

        var from = (Vector2) anchor + new Vector2(0.5f);
        var to = (Vector2) origin + new Vector2(0.5f);
        var angle = new Angle(turns * Math.PI / 2);
        var moving = new List<EntityUid>();
        var children = Transform(source).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            moving.Add(child);
        }

        foreach (var ent in moving)
        {
            var xform = Transform(ent);
            var anchored = xform.Anchored;
            var position = to + angle.RotateVec(xform.LocalPosition - from);
            _transform.SetCoordinates(ent, xform, new EntityCoordinates(outpost, position), xform.LocalRotation + angle);
            if (anchored && !xform.Anchored)
                _transform.AnchorEntity((ent, xform), (outpost.Owner, gridComp));
        }

        Del(source);
        CarveUnder((outpost, outpost.Comp, gridComp), placed.Select(tile => tile.Index).ToList());
        RebuildPortals((outpost, outpost.Comp, gridComp));
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
            $"{ToPrettyString(user):user} laid prefab {path} onto outpost {ToPrettyString(outpost):grid} at {origin}");
        reason = string.Empty;
        return true;
    }

    /// <summary>Loads a prefab's grid onto the staging map.</summary>
    private bool TryStagePrefab(ResPath path, out Entity<MapGridComponent> staged)
    {
        staged = default;
        MappingDataNode data;
        try
        {
            using var reader = new StringReader(_resources.ContentFileReadAllText(path));
            if (DataNodeParser.ParseYamlStream(reader).FirstOrDefault()?.Root is not MappingDataNode root)
                return false;
            data = root;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to read outpost prefab {path}: {e}");
            return false;
        }

        if (!Exists(_stageMap))
        {
            _stageMap = _maps.CreateMap(out var stageId);
            _maps.SetPaused(stageId, true);
            _stageOffset = 0;
        }

        _stageOffset += StageSpacing;
        if (!TryStageGrid(data, $"outpost prefab {path}", Transform(_stageMap.Value).MapID, new Vector2(_stageOffset, 0f), out var loaded))
            return false;

        staged = loaded.Value;
        return true;
    }

    /// <summary>
    /// Whether a prefab may land: the tile pointed at in the outpost's claim, and every tile not the outpost's floor yet
    /// and open ground no one else claims. A prefab wider than the claim's margin reaches past it, as a row of plates would.
    /// </summary>
    private bool IsClearForPrefab(Entity<WFOutpostComponent, MapGridComponent> outpost, Entity<MapGridComponent> ground,
        Vector2i origin, IEnumerable<Vector2i> tiles, out string reason)
    {
        var pointed = _transform.ToMapCoordinates(new EntityCoordinates(outpost, origin + new Vector2(0.5f)));
        if (!InClaim(outpost, _maps.TileIndicesFor(ground, ground.Comp, pointed)))
        {
            reason = "wf-outpost-plate-outside-claim";
            return false;
        }

        foreach (var index in tiles)
        {
            if (_maps.TryGetTileRef(outpost, outpost.Comp2, index, out var existing) && !existing.Tile.IsEmpty)
            {
                reason = "wf-outpost-prefab-overlaps";
                return false;
            }

            var world = _transform.ToMapCoordinates(new EntityCoordinates(outpost, index + new Vector2(0.5f)));
            var groundIndex = _maps.TileIndicesFor(ground, ground.Comp, world);
            if (!WithinSpan(outpost, outpost.Comp2, index))
            {
                reason = "wf-outpost-plate-too-wide";
                return false;
            }

            if (TryGetClaim(ground, groundIndex, outpost.Comp1.Owner, out _))
            {
                reason = "wf-outpost-plate-claimed";
                return false;
            }

            if (!IsOpenGround(ground, groundIndex, outpost, out reason))
                return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool FitsCaps(Entity<WFOutpostComponent, MapGridComponent> outpost, int tiles, int entities, out string reason)
    {
        if (CountAllTiles(outpost) + tiles > TileCap(outpost))
        {
            reason = "wf-outpost-plate-full";
            return false;
        }

        if (CountAllEntities((outpost, outpost.Comp1)) + entities > EntityCap(outpost))
        {
            reason = "wf-outpost-prefab-too-many";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Quarter turns counter-clockwise that point a prefab's north the way the user faces, on the outpost's grid.</summary>
    private static int Turns(Direction facing, Angle outpostRotation)
    {
        var local = (-outpostRotation).RotateVec(facing.ToVec());
        if (MathF.Abs(local.Y) >= MathF.Abs(local.X))
            return local.Y >= 0 ? 0 : 2;

        return local.X < 0 ? 1 : 3;
    }

    private static Vector2i Turn(Vector2i index, int turns)
    {
        for (var i = 0; i < turns; i++)
        {
            index = new Vector2i(-index.Y, index.X);
        }

        return index;
    }
}
