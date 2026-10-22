using Robust.Shared.Input.Binding;
using Robust.Shared.Map;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// A mode that works on the cells between a press and a release of the left button, with a live preview. Right
/// click and Escape drop the drag; when nothing is held the hovered cell is previewed.
/// </summary>
public abstract partial class WFMappingDragModeBase(WFMappingBuildModeSystem modes)
    : WFMappingModeHandlerBase(modes), IWFMappingPreviewSource
{
    protected bool Pressing { get; private set; }

    /// <summary>The grid the press landed on, or null off every grid.</summary>
    protected EntityUid? PressGrid { get; private set; }

    protected Vector2i PressCell { get; private set; }

    protected EntityCoordinates PressCoordinates { get; private set; }

    /// <summary>Where the press landed on the map.</summary>
    protected MapCoordinates PressPosition { get; private set; }

    protected EntityUid PressClicked { get; private set; }

    protected virtual Color PreviewColor => Color.FromHex("#4fc3f7");

    /// <summary>
    /// Whether the drag picks its grid from both ends (<see cref="WFMappingBuildModeSystem.ResolveShapeGrid"/>)
    /// instead of the press, so it may start in space; with no grid its cells are map tiles.
    /// </summary>
    protected virtual bool ResolvesGrid => false;

    public override void Exit()
    {
        Pressing = false;
    }

    public override bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (IsDown(args))
        {
            Pressing = false;
            var position = Modes.ToMap(args.Coordinates);
            EntityUid? grid;
            Vector2i cell;
            if (ResolvesGrid)
            {
                grid = Modes.ResolveShapeGrid(position, position);
                cell = Modes.CellAt(grid, position);
            }
            else
            {
                grid = Modes.TryGetGridCell(position, out var found, out cell) ? found.Owner : null;
            }

            if (ResolvesGrid && position.MapId == MapId.Nullspace || !CanStart(grid))
                return true;

            Pressing = true;
            PressGrid = grid;
            PressCell = cell;
            PressCoordinates = args.Coordinates;
            PressPosition = position;
            PressClicked = args.EntityUid;
            return true;
        }

        if (!Pressing)
            return true;

        Pressing = false;
        var (releaseGrid, start, end) = Span(Modes.ToMap(args.Coordinates));
        Release(releaseGrid, start, end);
        return true;
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        Pressing = false;
        return true;
    }

    public override bool Cancel()
    {
        if (!Pressing)
            return false;

        Pressing = false;
        return true;
    }

    public bool TryGetPreview(List<Vector2i> cells, out EntityUid grid, out Box2i? box, out Color color)
    {
        cells.Clear();
        box = null;
        color = PreviewColor;
        grid = default;

        if (Pressing)
        {
            var (pressGrid, start, end) = Span(Modes.MouseMapPosition);
            if (pressGrid == null && !ResolvesGrid || !TryGetFrame(pressGrid, PressPosition.MapId, out grid))
                return false;

            box = Preview(pressGrid, start, end, cells, ref color);
            return true;
        }

        var mouse = Modes.MouseMapPosition;
        if (ResolvesGrid)
        {
            if (mouse.MapId == MapId.Nullspace)
                return false;

            var hoverGrid = Modes.ResolveShapeGrid(mouse, mouse);
            if (!TryGetFrame(hoverGrid, mouse.MapId, out grid))
                return false;

            cells.Add(Modes.CellAt(hoverGrid, mouse));
            return true;
        }

        if (!Modes.TryGetGridCell(mouse, out var hovered, out var cell))
            return false;

        grid = hovered.Owner;
        cells.Add(cell);
        return true;
    }

    /// <summary>The entity whose frame the preview is drawn in: the grid, or the map for map tiles.</summary>
    private bool TryGetFrame(EntityUid? grid, MapId map, out EntityUid frame)
    {
        frame = grid ?? Modes.MapUid(map);
        return frame.IsValid();
    }

    /// <summary>The grid and the two corner cells of the drag from the press to <paramref name="end"/>.</summary>
    private (EntityUid? Grid, Vector2i Start, Vector2i End) Span(MapCoordinates end)
    {
        if (!ResolvesGrid)
        {
            var released = PressCell;
            if (PressGrid is { } pressGrid && Modes.TryGetCellOn(pressGrid, end, out var cell))
                released = cell;

            return (PressGrid, PressCell, released);
        }

        if (end.MapId != PressPosition.MapId)
            end = PressPosition;

        var grid = Modes.ResolveShapeGrid(PressPosition, end);
        return (grid, Modes.CellAt(grid, PressPosition), Modes.CellAt(grid, end));
    }

    /// <summary>Whether a press may start a drag here; refusals tell the player why.</summary>
    protected virtual bool CanStart(EntityUid? grid)
    {
        return true;
    }

    /// <summary>
    /// Fills the cells the drag would change, or returns a box to shade instead. <paramref name="grid"/> is null
    /// when the cells are map tiles.
    /// </summary>
    protected abstract Box2i? Preview(EntityUid? grid, Vector2i start, Vector2i end, List<Vector2i> cells,
        ref Color color);

    /// <summary>The button came up; <paramref name="start"/> equals <paramref name="end"/> for a plain click.</summary>
    protected abstract void Release(EntityUid? grid, Vector2i start, Vector2i end);

    /// <summary>The cells from one corner to the other, both included, as a box with right and top exclusive.</summary>
    protected static Box2i Corners(Vector2i a, Vector2i b)
    {
        return new Box2i(Vector2i.ComponentMin(a, b), Vector2i.ComponentMax(a, b) + Vector2i.One);
    }
}
