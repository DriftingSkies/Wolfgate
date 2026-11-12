using System.Numerics;
using Content.Server._WF.Outposts.Components;
using Content.Shared.Database;
using Content.Shared.Light.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Painting: floor and roof laid a batch of tiles at a time from the console's plan, under the same rules as foundation
/// plates and roof panels.
/// </summary>
public sealed partial class WFOutpostSystem
{
    /// <summary>
    /// Lays floor on the outpost for one of its builders: over its own floor, or out onto open ground in its claim.
    /// Returns the tiles that changed; <paramref name="reason"/> is the loc id of the first tile refused, if any.
    /// </summary>
    public List<Vector2i> PaintFloor(EntityUid user, Entity<WFOutpostComponent> outpost, IReadOnlyList<(Vector2i Index, string Tile)> cells,
        out string? reason)
    {
        reason = null;
        var done = new List<Vector2i>();
        var ground = outpost.Comp.Ground;
        if (!CanBuild(user, outpost) || !TryComp<MapGridComponent>(outpost, out var gridComp)
            || !TryComp<MapGridComponent>(ground, out var groundGrid))
        {
            reason = "wf-outpost-prefab-not-builder";
            return done;
        }

        var entity = (outpost.Owner, outpost.Comp, gridComp);
        var count = CountAllTiles(entity);
        var cap = TileCap(outpost);
        var added = new List<Vector2i>();
        foreach (var (index, tileId) in cells)
        {
            if (!_tiles.TryGetDefinition(tileId, out var def))
                continue;

            if (_maps.TryGetTileRef(outpost, gridComp, index, out var existing) && !existing.Tile.IsEmpty)
            {
                if (existing.Tile.TypeId == def.TileId)
                    continue;

                _maps.SetTile(outpost, gridComp, index, new Tile(def.TileId));
                done.Add(index);
                continue;
            }

            if (!CanExtendTo(entity, (ground, groundGrid), index, count, cap, out var refused))
            {
                reason ??= refused;
                continue;
            }

            _maps.SetTile(outpost, gridComp, index, new Tile(def.TileId));
            count++;
            added.Add(index);
            done.Add(index);
        }

        if (added.Count > 0)
        {
            CarveUnder(entity, added);
            RebuildPortals(entity);
        }

        if (done.Count > 0)
        {
            _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
                $"{ToPrettyString(user):user} painted {done.Count} floor tiles on outpost {ToPrettyString(outpost):grid}, {added.Count} of them new");
        }

        return done;
    }

    /// <summary>Whether the outpost's floor may grow onto the ground under one of its empty tiles.</summary>
    private bool CanExtendTo(Entity<WFOutpostComponent, MapGridComponent> outpost, Entity<MapGridComponent> ground, Vector2i index,
        int tiles, int cap, out string reason)
    {
        var world = _transform.ToMapCoordinates(new EntityCoordinates(outpost, index + new Vector2(0.5f)));
        var groundIndex = _maps.TileIndicesFor(ground, ground.Comp, world);
        if (!InClaim(outpost, groundIndex))
        {
            reason = "wf-outpost-plate-outside-claim";
            return false;
        }

        if (!WithinSpan(outpost, outpost.Comp2, index))
        {
            reason = "wf-outpost-plate-too-wide";
            return false;
        }

        if (tiles >= cap)
        {
            reason = "wf-outpost-plate-full";
            return false;
        }

        if (TryGetClaim(ground, groundIndex, outpost.Comp1.Owner, out _))
        {
            reason = "wf-outpost-plate-claimed";
            return false;
        }

        return IsOpenGround(ground, groundIndex, outpost, out reason);
    }

    /// <summary>Roofs or unroofs floor tiles of the outpost for one of its builders; returns the tiles that changed.</summary>
    public List<Vector2i> PaintRoof(EntityUid user, Entity<WFOutpostComponent> outpost, IReadOnlyList<Vector2i> cells, bool roofed)
    {
        var done = new List<Vector2i>();
        if (!CanBuild(user, outpost) || !TryComp<MapGridComponent>(outpost, out var gridComp) || !TryComp<RoofComponent>(outpost, out var roof))
            return done;

        foreach (var index in cells)
        {
            if (!_maps.TryGetTileRef(outpost, gridComp, index, out var tile) || tile.Tile.IsEmpty
                || _roof.IsRooved((outpost.Owner, gridComp, roof), index) == roofed)
                continue;

            _roof.SetRoof((outpost.Owner, gridComp, roof), index, roofed);
            done.Add(index);
        }

        return done;
    }
}
