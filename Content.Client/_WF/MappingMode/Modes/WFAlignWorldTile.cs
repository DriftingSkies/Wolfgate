using System.Numerics;
using Robust.Client.Placement;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// Tile placement for mapping sessions: on a grid it snaps to that grid's tiles, off every grid to the whole world
/// tile under the cursor, on the grid the tile will join.
/// </summary>
public sealed class WFAlignWorldTile : PlacementMode
{
    public override bool HasLineMode => true;
    public override bool HasGridMode => true;

    public WFAlignWorldTile(PlacementManager pMan) : base(pMan) { }

    public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
    {
        var entities = pManager.EntityManager;
        var transform = entities.System<SharedTransformSystem>();
        var map = entities.System<SharedMapSystem>();
        var cursor = ScreenToCursorGrid(mouseScreen);
        var mapCoords = transform.ToMapCoordinates(cursor);
        GridDistancing = 1f;

        if (map.TryFindGridAt(mapCoords, out var gridUid, out var grid))
        {
            CurrentTile = map.GetTileRef(gridUid, grid, cursor);
            MouseCoords = map.GridTileToLocal(gridUid, grid, CurrentTile.GridIndices);
            return;
        }

        var centre = new Vector2(MathF.Floor(mapCoords.X) + 0.5f, MathF.Floor(mapCoords.Y) + 0.5f);
        var client = entities.System<WFMappingClientSystem>();
        if (client.TileTarget(mapCoords.MapId, centre) is { } target)
        {
            MouseCoords = new EntityCoordinates(target, Vector2.Transform(centre, transform.GetInvWorldMatrix(target)));
            return;
        }

        MouseCoords = new EntityCoordinates(map.GetMapOrInvalid(mapCoords.MapId), centre);
    }

    public override bool IsValidPosition(EntityCoordinates position)
    {
        return RangeCheck(position);
    }
}
