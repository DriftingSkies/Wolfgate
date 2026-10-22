using Content.Client._WF.MappingMode.Link;
using Content.Shared._WF.MappingMode;
using Robust.Client.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// Link: configure signal links, device lists and access by clicking. Tab or Properties picks the sub-tool; the
/// work is done by <see cref="WFMappingLinkSystem"/>. In Devices a drag adds every device in the box.
/// </summary>
public sealed partial class WFMappingLinkMode(WFMappingBuildModeSystem modes) : WFMappingModeHandlerBase(modes)
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IInputManager _input = default!;

    /// <summary>How far a press must move, in tiles, to be a box instead of a click.</summary>
    private const float DragDistance = 0.75f;

    private bool _pressing;
    private EntityCoordinates _pressCoordinates;
    private MapCoordinates _pressPosition;
    private EntityUid _pressClicked;

    private WFMappingLinkSystem Link => _entities.System<WFMappingLinkSystem>();

    public override WFMappingBuildMode Mode => WFMappingBuildMode.Link;

    public override void Enter()
    {
        var context = _input.Contexts.GetContext("common");
        if (!context.FunctionExists(WFMappingKeyFunctions.WFMappingLinkCycle))
            context.AddFunction(WFMappingKeyFunctions.WFMappingLinkCycle);

        Link.SetModeActive(true);
    }

    public override void Exit()
    {
        _pressing = false;
        var context = _input.Contexts.GetContext("common");
        if (context.FunctionExists(WFMappingKeyFunctions.WFMappingLinkCycle))
            context.RemoveFunction(WFMappingKeyFunctions.WFMappingLinkCycle);

        Link.SetModeActive(false);
    }

    public override bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (IsDown(args))
        {
            _pressing = true;
            _pressCoordinates = args.Coordinates;
            _pressPosition = Modes.ToMap(args.Coordinates);
            _pressClicked = args.EntityUid;
            return true;
        }

        if (!_pressing)
            return true;

        _pressing = false;
        var release = Modes.ToMap(args.Coordinates);
        if (IsBox(_pressPosition, release))
            Link.AddBox(_pressPosition, release);
        else
            Link.Click(_pressCoordinates, _pressClicked);

        return true;
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (!IsDown(args))
            return true;

        if (_pressing)
            _pressing = false;
        else
            Link.RightClick(args.Coordinates, args.EntityUid);

        return true;
    }

    public override bool Cancel()
    {
        if (!_pressing)
            return Link.ClearSelection();

        _pressing = false;
        return true;
    }

    /// <summary>The corners of the box being dragged in Devices, if there is one.</summary>
    public bool TryGetBox(out MapCoordinates start, out MapCoordinates end)
    {
        start = _pressPosition;
        end = Modes.MouseMapPosition;
        return _pressing && IsBox(start, end);
    }

    private bool IsBox(MapCoordinates start, MapCoordinates end)
    {
        return Link is { Tool: WFMappingLinkTool.Devices, Holder: not null } &&
               start.MapId == end.MapId &&
               start.MapId != MapId.Nullspace &&
               (end.Position - start.Position).Length() >= DragDistance;
    }
}
