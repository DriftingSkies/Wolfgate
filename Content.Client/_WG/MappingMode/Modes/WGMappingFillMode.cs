using Content.Shared._WG.MappingMode;
using Content.Shared._WG.MappingTools;
using Robust.Shared.Input.Binding;

namespace Content.Client._WG.MappingMode.Modes;

/// <summary>
/// Fill: a left click on a grid floods the touching cells of the clicked tile type, empty included, with the
/// selected tile. The flood stays inside the grid's tiles' bounds and the map area, and stops at the draw limit.
/// </summary>
public sealed partial class WGMappingFillMode(WGMappingBuildModeSystem modes) : WGMappingModeHandlerBase(modes)
{
    [Dependency] private IEntityManager _entities = default!;

    public override WGMappingBuildMode Mode => WGMappingBuildMode.Fill;

    public override bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (!IsDown(args))
            return true;

        if (!Modes.TryGetTile(Modes.Entry, out var tile))
        {
            Modes.Popup("wg-mapping-screen-fill-needs-tile");
            return true;
        }

        if (!Modes.TryGetGridCell(Modes.ToMap(args.Coordinates), out var grid, out var start))
        {
            Modes.Popup("wg-mapping-screen-needs-grid");
            return true;
        }

        var rotation = Modes.TileRotation(tile);
        if (Modes.TileTypeAt(grid, start) == tile.TileId)
            return true;

        var cells = MappingToolsShapes.Flood(start, cell => Modes.TileTypeAt(grid, cell), Modes.FillBounds(grid),
            WGMappingModeLimits.MaxDrawCells, out var truncated);
        if (cells.Count == 0)
            return true;

        if (truncated)
            Modes.Popup("wg-mapping-screen-fill-truncated", ("max", WGMappingModeLimits.MaxDrawCells));

        Modes.SendDraw(new MappingToolsDrawEvent(_entities.GetNetEntity(grid), MappingDrawOp.SetTiles, cells,
            tile.ID, rotation, null, Angle.Zero));
        return true;
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        return true;
    }
}
