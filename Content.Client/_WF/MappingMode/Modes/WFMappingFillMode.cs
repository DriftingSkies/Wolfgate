using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Robust.Shared.Input.Binding;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// Fill: a left click on a grid floods the touching cells of the clicked tile type, empty included, with the
/// selected tile. The flood stays inside the grid's tiles' bounds and the map area, and stops at the draw limit.
/// </summary>
public sealed partial class WFMappingFillMode(WFMappingBuildModeSystem modes) : WFMappingModeHandlerBase(modes)
{
    [Dependency] private IEntityManager _entities = default!;

    public override WFMappingBuildMode Mode => WFMappingBuildMode.Fill;

    public override bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (!IsDown(args))
            return true;

        if (!Modes.TryGetTile(Modes.Entry, out var tile))
        {
            Modes.Popup("wf-mapping-screen-fill-needs-tile");
            return true;
        }

        if (!Modes.TryGetGridCell(Modes.ToMap(args.Coordinates), out var grid, out var start))
        {
            Modes.Popup("wf-mapping-screen-needs-grid");
            return true;
        }

        var rotation = Modes.TileRotation(tile);
        if (Modes.TileTypeAt(grid, start) == tile.TileId)
            return true;

        var cells = MappingToolsShapes.Flood(start, cell => Modes.TileTypeAt(grid, cell), Modes.FillBounds(grid),
            WFMappingModeLimits.MaxDrawCells, out var truncated);
        if (cells.Count == 0)
            return true;

        if (truncated)
            Modes.Popup("wf-mapping-screen-fill-truncated", ("max", WFMappingModeLimits.MaxDrawCells));

        Modes.SendDraw(new MappingToolsDrawEvent(_entities.GetNetEntity(grid), MappingDrawOp.SetTiles, cells,
            tile.ID, rotation, null, Angle.Zero));
        return true;
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        return true;
    }
}
