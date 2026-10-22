using Content.Client._WF.MappingMode.Palette;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Robust.Shared.Input.Binding;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// Shape: drags a line, rectangle or circle of the selected tile or entity, whole or as a border, sent as one
/// request when the button comes up. The shape goes on the grid under either end or touching it, so a tile shape
/// can grow a grid from space; with no grid near, tiles make a new one. Shift keeps rectangles square and circles
/// round; a right click with nothing held flips between fill and border.
/// </summary>
public sealed partial class WFMappingShapeMode(WFMappingBuildModeSystem modes) : WFMappingDragModeBase(modes)
{
    [Dependency] private IEntityManager _entities = default!;

    private static readonly Color TooMany = Color.FromHex("#ff1744");

    /// <summary>Boxes larger than this aren't turned into circles at all; no circle in them fits the cap anyway.</summary>
    private const long MaxCircleBox = 1L << 22;

    private (Vector2i Start, Vector2i End, bool Border)? _circleKey;
    private List<Vector2i>? _circleCells;

    public override WFMappingBuildMode Mode => WFMappingBuildMode.Shape;

    protected override Color PreviewColor => Color.FromHex("#66bb6a");

    protected override bool ResolvesGrid => true;

    private bool Border => Modes.ShapeFill == WFMappingShapeFill.Border;

    protected override bool CanStart(EntityUid? grid)
    {
        if (Modes.Entry is not { Kind: WFMappingPaletteKind.Tile or WFMappingPaletteKind.Entity })
        {
            Modes.Popup("wf-mapping-screen-shape-needs-entry");
            return false;
        }

        return true;
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (IsDown(args) && !Pressing && Modes.ShapeKind != WFMappingShapeKind.Line)
            Modes.SetShapeFill(Border ? WFMappingShapeFill.Fill : WFMappingShapeFill.Border);

        return base.Secondary(args);
    }

    protected override Box2i? Preview(EntityUid? grid, Vector2i start, Vector2i end, List<Vector2i> cells,
        ref Color color)
    {
        end = Constrain(start, end);
        if (grid == null && Modes.Entry is { Kind: WFMappingPaletteKind.Entity })
            color = TooMany;

        switch (Modes.ShapeKind)
        {
            case WFMappingShapeKind.Line:
                cells.AddRange(MappingToolsShapes.Line(start, end));
                if (cells.Count > WFMappingModeLimits.MaxDrawCells)
                    color = TooMany;

                return null;
            case WFMappingShapeKind.Circle:
                if (Circle(start, end) is not { } circle)
                {
                    color = TooMany;
                    return Corners(start, end);
                }

                if (circle.Count > WFMappingModeLimits.MaxDrawCells)
                    color = TooMany;

                cells.AddRange(circle);
                return null;
            default:
                if (MappingToolsShapes.RectCount(start, end, Border) > WFMappingModeLimits.MaxDrawCells)
                    color = TooMany;

                // A filled rectangle is shaded as one box; a border shows its cells.
                if (!Border)
                    return Corners(start, end);

                cells.AddRange(MappingToolsShapes.Rect(start, end, true));
                return null;
        }
    }

    protected override void Release(EntityUid? grid, Vector2i start, Vector2i end)
    {
        if (Modes.Entry is not { } entry)
            return;

        end = Constrain(start, end);
        var count = Modes.ShapeKind switch
        {
            WFMappingShapeKind.Line => Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y)) + 1,
            WFMappingShapeKind.Circle => Circle(start, end)?.Count ?? long.MaxValue,
            _ => MappingToolsShapes.RectCount(start, end, Border),
        };
        if (count > WFMappingModeLimits.MaxDrawCells)
        {
            Modes.Popup("wf-mapping-screen-too-many", ("max", WFMappingModeLimits.MaxDrawCells));
            return;
        }

        var cells = Modes.ShapeKind switch
        {
            WFMappingShapeKind.Line => MappingToolsShapes.Line(start, end),
            WFMappingShapeKind.Circle => new List<Vector2i>(Circle(start, end)!),
            _ => MappingToolsShapes.Rect(start, end, Border),
        };
        NetEntity? net = grid is { } gridUid ? _entities.GetNetEntity(gridUid) : null;
        var map = _entities.GetNetEntity(Modes.MapUid(PressPosition.MapId));

        if (Modes.TryGetTile(entry, out var tile))
        {
            Modes.SendDraw(new MappingToolsDrawEvent(net, MappingDrawOp.SetTiles, cells, tile.ID,
                Modes.TileRotation(tile), null, Angle.Zero) { Map = map });
            return;
        }

        // Entities only go on tiles; the server skips cells without one.
        if (net == null)
        {
            Modes.Popup("wf-mapping-screen-shape-entities-need-grid");
            return;
        }

        Modes.SendDraw(new MappingToolsDrawEvent(net, MappingDrawOp.SpawnEntities, cells, null, 0, entry.Id,
            Modes.PlacementRotation));
    }

    /// <summary>With Shift held, moves the end so a rectangle or circle has equal sides.</summary>
    private Vector2i Constrain(Vector2i start, Vector2i end)
    {
        if (Modes.ShapeKind == WFMappingShapeKind.Line || !Modes.ShiftDown)
            return end;

        var delta = end - start;
        var side = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
        return start + new Vector2i(delta.X < 0 ? -side : side, delta.Y < 0 ? -side : side);
    }

    /// <summary>The circle's cells, kept while the drag doesn't change; null when the box is far too big.</summary>
    private List<Vector2i>? Circle(Vector2i start, Vector2i end)
    {
        var size = Vector2i.ComponentMax(start, end) - Vector2i.ComponentMin(start, end) + Vector2i.One;
        if ((long) size.X * size.Y > MaxCircleBox)
            return null;

        var key = (start, end, Border);
        if (_circleKey != key || _circleCells == null)
        {
            _circleKey = key;
            _circleCells = MappingToolsShapes.Ellipse(start, end, Border);
        }

        return _circleCells;
    }

    public override bool Wheel(float delta)
    {
        return Modes.CtrlDown && Modes.RotatePlacement(delta > 0 ? 1 : -1);
    }
}
