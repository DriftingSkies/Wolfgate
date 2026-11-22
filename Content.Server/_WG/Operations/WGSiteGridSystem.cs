using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.Operations;

/// <summary>Helpers for event sites that load a grid and fill it: operations and Monolith fragments.</summary>
public sealed class WGSiteGridSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;

    /// <summary>The entities parented to the grid, copied so the caller may change them.</summary>
    public List<EntityUid> Children(EntityUid grid)
    {
        var children = new List<EntityUid>();
        var enumerator = Transform(grid).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
            children.Add(child);
        return children;
    }

    /// <summary>Floor tiles on the grid that nothing blocks.</summary>
    public List<EntityCoordinates> OpenTiles(Entity<MapGridComponent> grid)
    {
        var tiles = new List<EntityCoordinates>();
        foreach (var tile in _map.GetAllTiles(grid, grid.Comp))
        {
            if (IsOpen(tile))
                tiles.Add(_map.GridTileToLocal(grid, grid.Comp, tile.GridIndices));
        }
        return tiles;
    }

    /// <summary>Whether a tile is floor that nothing blocks.</summary>
    public bool IsOpen(TileRef tile)
    {
        return !_turf.IsSpace(tile) && !_turf.IsTileBlocked(tile, CollisionGroup.Impassable);
    }
}
