using Content.Server._WF.Outposts.Components;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.Outposts;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Foundation plates: the outpost grows a tile at a time onto open ground anywhere in its claim, so its floor may lie in
/// separate patches, as long as the whole spans no more than <see cref="SpanCap"/> tiles either way.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private SharedStackSystem _stack = default!;

    private static readonly Vector2i[] Neighbours = { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right };

    private void InitializeFoundation()
    {
        SubscribeLocalEvent<WFOutpostFoundationPlateComponent, AfterInteractEvent>(OnPlateAfterInteract);
    }

    private void OnPlateAfterInteract(Entity<WFOutpostFoundationPlateComponent> plate, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        args.Handled = true;
        if (!TryExtend(args.User, plate.Comp.Tile, args.ClickLocation, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
            return;
        }

        UseOne(plate);
    }

    /// <summary>Uses up one plate or panel: one off its stack, or the item itself.</summary>
    private void UseOne(EntityUid item)
    {
        if (!TryComp<StackComponent>(item, out var stack) || !_stack.Use(item, 1, stack))
            QueueDel(item);
    }

    /// <summary>
    /// Extends an outpost onto the ground tile at <paramref name="target"/>, which has to be open ground in its claim:
    /// one the user is a listed builder on, if the tile is in its claim, else the user's own. The ground there is carved
    /// away. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryExtend(EntityUid user, string tileId, EntityCoordinates target, out string reason)
    {
        // A plate laid against the rock of a basement digs it out instead.
        if (HasComp<WFCavernLayerComponent>(_transform.GetMap(target)))
            return TryExtendBasement(user, tileId, target, out reason);

        if (!TryGetPlateOutpost(user, target, out var outpost) || !TryComp<MapGridComponent>(outpost, out var gridComp))
        {
            reason = "wf-outpost-plate-not-outpost";
            return false;
        }

        var ground = outpost.Comp.Ground;
        if (_transform.GetMap(target) != ground || !TryComp<MapGridComponent>(ground, out var groundGrid))
        {
            reason = "wf-outpost-plate-wrong-ground";
            return false;
        }

        var at = _transform.ToMapCoordinates(target);
        var index = _maps.TileIndicesFor(outpost, gridComp, at);
        if (_maps.TryGetTileRef(outpost, gridComp, index, out var existing) && !existing.Tile.IsEmpty)
        {
            reason = "wf-outpost-plate-taken";
            return false;
        }

        var groundIndex = _maps.TileIndicesFor(ground, groundGrid, at);
        if (!InClaim((outpost, outpost.Comp, gridComp), groundIndex))
        {
            reason = "wf-outpost-plate-outside-claim";
            return false;
        }

        if (!WithinSpan(outpost, gridComp, index))
        {
            reason = "wf-outpost-plate-too-wide";
            return false;
        }

        if (CountAllTiles((outpost, outpost.Comp, gridComp)) >= TileCap(outpost))
        {
            reason = "wf-outpost-plate-full";
            return false;
        }

        if (TryGetClaim(ground, groundIndex, outpost.Comp.Owner, out _))
        {
            reason = "wf-outpost-plate-claimed";
            return false;
        }

        if (!IsOpenGround((ground, groundGrid), groundIndex, outpost, out reason))
            return false;

        _maps.SetTile(outpost, gridComp, index, new Tile(_tiles[tileId].TileId));
        CarveUnder((outpost, outpost.Comp, gridComp), new List<Vector2i> { index });
        RebuildPortals((outpost, outpost.Comp, gridComp));
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low, $"{ToPrettyString(user):user} extended outpost {ToPrettyString(outpost):grid} to {index}");
        return true;
    }

    /// <summary>The outpost a plate at <paramref name="target"/> extends: one the user builds on whose claim it is in, else their own.</summary>
    private bool TryGetPlateOutpost(EntityUid user, EntityCoordinates target, out Entity<WFOutpostComponent> outpost)
    {
        var map = _transform.GetMap(target);
        var at = _transform.ToMapCoordinates(target);
        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var comp, out var grid))
        {
            if (comp.Ground != map || IsOwnerOf(user, comp) || !CanBuild(user, (uid, comp)))
                continue;

            if (!TryComp<MapGridComponent>(comp.Ground, out var groundGrid)
                || !InClaim((uid, comp, grid), _maps.TileIndicesFor(comp.Ground, groundGrid, at)))
                continue;

            outpost = (uid, comp);
            return true;
        }

        if (TryComp<ActorComponent>(user, out var actor) && TryGetOutpost(actor.PlayerSession.UserId, out outpost))
            return true;

        outpost = default;
        return false;
    }

    /// <summary>Whether a ground tile is in an outpost's claim.</summary>
    private bool InClaim(Entity<WFOutpostComponent, MapGridComponent> outpost, Vector2i groundIndex)
    {
        var (min, max) = GetClaimZone(outpost);
        return groundIndex.X >= min.X && groundIndex.Y >= min.Y && groundIndex.X <= max.X && groundIndex.Y <= max.Y;
    }

    /// <summary>Whether the outpost's floor, with a tile added at <paramref name="index"/>, still spans no more than the cap either way.</summary>
    private bool WithinSpan(EntityUid outpost, MapGridComponent grid, Vector2i index)
    {
        var local = grid.LocalAABB;
        var size = grid.TileSize;
        var min = Vector2i.ComponentMin(new Vector2i((int) MathF.Floor(local.Left / size), (int) MathF.Floor(local.Bottom / size)), index);
        var max = Vector2i.ComponentMax(new Vector2i((int) MathF.Ceiling(local.Right / size) - 1, (int) MathF.Ceiling(local.Top / size) - 1), index);
        var span = SpanCap(outpost);
        return max.X - min.X < span && max.Y - min.Y < span;
    }
}
