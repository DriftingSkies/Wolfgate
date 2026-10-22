using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Robust.Client.Graphics;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// Pipe: drag a path of cells on a grid and the server lays plain gas pipes along it, joined to the pipes around it.
/// Middle click, or a left click after Properties' paint button, paints the clicked pipe's whole network. With a
/// device armed from Properties' device box, a left click places that device instead.
/// </summary>
public sealed partial class WFMappingPipeMode(WFMappingBuildModeSystem modes)
    : WFMappingModeHandlerBase(modes), IWFMappingPreviewSource, IWFMappingFrameHandler
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IOverlayManager _overlays = default!;

    private static readonly Color NoColor = Color.FromHex("#b0bec5");
    private static readonly Color TooMany = Color.FromHex("#ff1744");

    private readonly List<Vector2i> _path = new();
    private EntityUid? _grid;
    private bool _pressing;
    private bool _overflow;

    public override WFMappingBuildMode Mode => WFMappingBuildMode.Pipe;

    /// <summary>Whether a path is being dragged.</summary>
    public bool Pressing => _pressing;

    public override void Enter()
    {
        if (!_overlays.HasOverlay<WFMappingPipeDeviceOverlay>())
            _overlays.AddOverlay(new WFMappingPipeDeviceOverlay(Modes, this));
    }

    /// <summary>
    /// The colour of a preset: the standard atmos pipe colours of the spray painter
    /// (<c>Resources/Prototypes/Entities/Objects/Tools/spray_painter.yml</c>).
    /// </summary>
    public static Color PresetColor(WFMappingPipePreset preset)
    {
        return preset switch
        {
            WFMappingPipePreset.Distro => Color.FromHex("#0055CC"),
            WFMappingPipePreset.Waste => Color.FromHex("#990000"),
            WFMappingPipePreset.Air => Color.FromHex("#03FCD3"),
            WFMappingPipePreset.Mix => Color.FromHex("#947507"),
            _ => Color.White,
        };
    }

    public override void Exit()
    {
        Drop();
        Modes.SetPipePainting(false);
        _overlays.RemoveOverlay<WFMappingPipeDeviceOverlay>();
    }

    public override bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (IsDown(args))
        {
            Drop();
            if (!Modes.TryGetGridCell(Modes.ToMap(args.Coordinates), out var grid, out var cell))
            {
                Modes.Popup("wf-mapping-screen-needs-grid");
                return true;
            }

            if (Modes.PipePainting)
            {
                Modes.SetPipePainting(false);
                Paint(grid.Owner, cell);
                return true;
            }

            if (Modes.PipeDevice is { } device)
            {
                Modes.SendDraw(new MappingToolsDrawEvent(_entities.GetNetEntity(grid.Owner), MappingDrawOp.PipeDevice,
                    new List<Vector2i> { cell }, null, 0, device, Angle.Zero) { Pipes = Modes.PipeSpec });
                return true;
            }

            _grid = grid.Owner;
            _pressing = true;
            _path.Add(cell);
            return true;
        }

        if (!_pressing)
            return true;

        Follow(Modes.ToMap(args.Coordinates));
        _pressing = false;
        Send();
        return true;
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (IsDown(args) && !_pressing)
        {
            Modes.SetPipePainting(false);
            Modes.SetPipeDevice(null);
        }

        Drop();
        return true;
    }

    public override bool Wheel(float delta)
    {
        if (!Modes.CtrlDown)
            return false;

        Modes.RotatePipeFacing(delta > 0 ? 1 : -1);
        return true;
    }

    public override bool Middle(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (!IsDown(args) || _pressing)
            return true;

        if (Modes.TryGetGridCell(Modes.ToMap(args.Coordinates), out var grid, out var cell))
            Paint(grid.Owner, cell);
        else
            Modes.Popup("wf-mapping-screen-needs-grid");

        return true;
    }

    public override bool Cancel()
    {
        if (_pressing)
        {
            Drop();
            return true;
        }

        if (Modes.PipeDevice != null)
        {
            Modes.SetPipeDevice(null);
            return true;
        }

        if (!Modes.PipePainting)
            return false;

        Modes.SetPipePainting(false);
        return true;
    }

    public void FrameUpdate()
    {
        if (_pressing)
            Follow(Modes.MouseMapPosition);
    }

    public bool TryGetPreview(List<Vector2i> cells, out EntityUid grid, out Box2i? box, out Color color)
    {
        cells.Clear();
        box = null;
        grid = default;
        color = Modes.PipeColor ?? NoColor;

        if (_pressing && _grid is { } pressGrid)
        {
            grid = pressGrid;
            cells.AddRange(_path);
            if (_overflow)
                color = TooMany;

            return true;
        }

        if (!Modes.TryGetGridCell(Modes.MouseMapPosition, out var hovered, out var cell))
            return false;

        grid = hovered.Owner;
        cells.Add(cell);
        return true;
    }

    /// <summary>Adds the cell under a map position to the path, on the press's grid.</summary>
    private void Follow(MapCoordinates position)
    {
        if (_grid is not { } grid || !Modes.TryGetCellOn(grid, position, out var cell))
            return;

        if (!MappingToolsPipes.Extend(_path, cell, WFMappingModeLimits.MaxDrawCells))
            _overflow = true;
    }

    private void Send()
    {
        if (_grid is not { } grid || _path.Count == 0)
        {
            Drop();
            return;
        }

        if (_overflow)
        {
            Modes.Popup("wf-mapping-screen-too-many", ("max", WFMappingModeLimits.MaxDrawCells));
            Drop();
            return;
        }

        Modes.SendDraw(new MappingToolsDrawEvent(_entities.GetNetEntity(grid), MappingDrawOp.Pipes,
            new List<Vector2i>(_path), null, 0, null, Angle.Zero) { Pipes = Modes.PipeSpec });
        Drop();
    }

    /// <summary>Asks the server to paint the network of the pipe on a cell with the current colour.</summary>
    private void Paint(EntityUid grid, Vector2i cell)
    {
        if (Modes.PipeColor == null)
        {
            Modes.Popup("wf-mapping-screen-pipe-paint-needs-color");
            return;
        }

        Modes.SendDraw(new MappingToolsDrawEvent(_entities.GetNetEntity(grid), MappingDrawOp.PipeColor,
            new List<Vector2i> { cell }, null, 0, null, Angle.Zero) { Pipes = Modes.PipeSpec });
    }

    private void Drop()
    {
        _pressing = false;
        _overflow = false;
        _grid = null;
        _path.Clear();
    }
}
