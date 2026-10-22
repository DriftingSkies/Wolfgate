using System.Linq;
using System.Numerics;
using Content.Shared._WF.MappingMode;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    private readonly WFMappingThrottle<NetUserId> _gridThrottle = new(2, 4);

    private void InitializeGrids()
    {
        SubscribeNetworkEvent<WFMappingGridListRequest>(OnGridListRequest);
        SubscribeNetworkEvent<WFMappingGoToGridRequest>(OnGoToGridRequest);
    }

    /// <summary>The grids directly on a map, most tiles first.</summary>
    public List<WFMappingGridInfo> GridsOn(EntityUid map)
    {
        var grids = new List<WFMappingGridInfo>();
        var children = Transform(map).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<MapGridComponent>(child, out var grid))
                continue;

            grids.Add(new WFMappingGridInfo(GetNetEntity(child), MetaData(child).EntityName, CountTiles(child, grid),
                CountGridEntities(child)));
        }

        grids.Sort((a, b) => b.Tiles.CompareTo(a.Tiles));
        return grids;
    }

    /// <summary>Everything under a grid, contents included, but not the players standing on it.</summary>
    private int CountGridEntities(EntityUid grid)
    {
        var count = 0;
        var stack = new Stack<EntityUid>();
        stack.Push(grid);
        while (stack.TryPop(out var uid))
        {
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (HasComp<ActorComponent>(child) || HasComp<WFMappingObserverComponent>(child))
                    continue;

                count++;
                stack.Push(child);
            }
        }

        return count;
    }

    private int CountTiles(EntityUid uid, MapGridComponent grid)
    {
        var count = 0;
        var tiles = _map.GetAllTiles(uid, grid);
        while (tiles.MoveNext(out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>The centre of a grid's bounds in world space.</summary>
    public Vector2 GridCentre(EntityUid grid)
    {
        return _transform.GetWorldMatrix(grid).TransformBox(Comp<MapGridComponent>(grid).LocalAABB).Center;
    }

    /// <summary>The centre of the bounds of the grid with the most tiles, or null when there is no grid.</summary>
    public Vector2? LargestGridCentre(IEnumerable<EntityUid> grids)
    {
        EntityUid? largest = null;
        var most = -1;
        foreach (var uid in grids)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<MapGridComponent>(uid, out var grid))
                continue;

            var tiles = CountTiles(uid, grid);
            if (tiles <= most)
                continue;

            most = tiles;
            largest = uid;
        }

        return largest is { } found ? GridCentre(found) : null;
    }

    /// <summary>The centre of the largest grid directly on a map, or null when it has none.</summary>
    public Vector2? LargestGridCentre(EntityUid map)
    {
        var grids = new List<EntityUid>();
        var children = Transform(map).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            grids.Add(child);
        }

        return LargestGridCentre(grids);
    }

    private void OnGridListRequest(WFMappingGridListRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetPresent(player, out var session) ||
            !TryThrottle(_gridThrottle, player, "grid list") ||
            player.AttachedEntity is not { } observer ||
            Transform(observer).MapUid is not { } map ||
            !IsSessionMap(map, session))
        {
            return;
        }

        RaiseNetworkEvent(new WFMappingGridListEvent(GridsOn(map)), player);
    }

    private void OnGoToGridRequest(WFMappingGoToGridRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetPresent(player, out var session) ||
            !TryThrottle(_gridThrottle, player, "go to grid") ||
            player.AttachedEntity is not { } observer)
        {
            return;
        }

        if (!TryGetEntity(ev.Grid, out var grid) ||
            TerminatingOrDeleted(grid) ||
            !HasComp<MapGridComponent>(grid) ||
            Transform(grid.Value).MapUid is not { } map ||
            !IsSessionMap(map, session))
        {
            Refuse(player, "wf-mapping-ship-goto-not-yours");
            return;
        }

        MoveObserver(observer, map, GridCentre(grid.Value));
    }

    /// <summary>Puts an observer at a world position on a map.</summary>
    private void MoveObserver(EntityUid observer, EntityUid map, Vector2 position)
    {
        _transform.SetMapCoordinates(observer, new MapCoordinates(position, Comp<MapComponent>(map).MapId));
    }

    /// <summary>Puts a player's observer at the centre of the largest of some grids, if they are present.</summary>
    private void MoveToLargest(ICommonSession player, EntityUid map, IEnumerable<EntityUid> grids)
    {
        if (player.AttachedEntity is { } observer && LargestGridCentre(grids.ToList()) is { } centre)
            MoveObserver(observer, map, centre);
    }
}
