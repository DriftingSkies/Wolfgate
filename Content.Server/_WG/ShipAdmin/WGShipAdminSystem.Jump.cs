using System.Numerics;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.ShipAdmin;

public sealed partial class WGShipAdminSystem
{
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private TurfSystem _turfs = default!;

    /// <summary>
    /// Puts the admin's body on the ship. A ghost goes to its middle; a body in the flesh goes to the open floor
    /// nearest the middle, so it doesn't land in a wall.
    /// </summary>
    public bool JumpTo(ICommonSession admin, EntityUid grid, out string result)
    {
        if (admin.AttachedEntity is not { Valid: true } body || !TryComp(body, out TransformComponent? bodyXform))
        {
            result = Loc.GetString("wg-ship-admin-result-move-no-body");
            return false;
        }

        if (!TryJumpSpot(grid, !HasComp<GhostComponent>(body), out var spot))
        {
            result = Loc.GetString("wg-ship-admin-result-jump-failed", ("ship", Name(grid)));
            return false;
        }

        _transform.SetMapCoordinates(body, spot);
        _transform.AttachToGridOrMap(body, bodyXform);
        Audit(admin, LogImpact.Low, grid, "jumped to it");
        result = Loc.GetString("wg-ship-admin-result-jumped", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Where a jump to the grid lands: its middle, or for a body that needs floor, the clear tile nearest it.</summary>
    private bool TryJumpSpot(EntityUid grid, bool needsFloor, out MapCoordinates spot)
    {
        var xform = Transform(grid);
        spot = MapCoordinates.Nullspace;
        if (xform.MapUid == null || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        // A planet's surface is its own map; its middle is wherever the admin last looked, so take its origin.
        var middle = HasComp<MapComponent>(grid)
            ? Vector2.Zero
            : CompOrNull<PhysicsComponent>(grid)?.LocalCenter ?? gridComp.LocalAABB.Center;

        spot = _transform.ToMapCoordinates(new EntityCoordinates(grid, middle));
        if (!needsFloor || HasComp<MapComponent>(grid))
            return true;

        var tiles = new List<(float Distance, TileRef Tile)>();
        var enumerator = _maps.GetAllTilesEnumerator(grid, gridComp);
        while (enumerator.MoveNext(out var tile))
        {
            if (_turfs.IsSpace(tile.Value))
                continue;

            tiles.Add(((TileCentre(tile.Value, gridComp) - middle).LengthSquared(), tile.Value));
        }

        tiles.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        foreach (var (_, tile) in tiles)
        {
            if (_turfs.IsTileBlocked(tile, CollisionGroup.Impassable | CollisionGroup.MidImpassable))
                continue;

            spot = _transform.ToMapCoordinates(new EntityCoordinates(grid, TileCentre(tile, gridComp)));
            return true;
        }

        // No open floor: a body would land in a wall or in space, so it stays where it is.
        return false;
    }

    private static Vector2 TileCentre(TileRef tile, MapGridComponent grid)
    {
        return (Vector2) tile.GridIndices * grid.TileSize + new Vector2(grid.TileSize / 2f);
    }
}
