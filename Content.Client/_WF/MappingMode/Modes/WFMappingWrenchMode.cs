using Content.Shared._WF.MappingTools;
using Robust.Shared.Input.Binding;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>Tools: a left click uses the picked tool (wrench, bolts, weld, lock, panel, door) on what is under it.</summary>
public sealed partial class WFMappingWrenchMode(WFMappingBuildModeSystem modes) : WFMappingModeHandlerBase(modes)
{
    [Dependency] private IEntityManager _entities = default!;

    public override WFMappingBuildMode Mode => WFMappingBuildMode.Wrench;

    public override bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (!IsDown(args))
            return true;

        var clicked = _entities.EntityExists(args.EntityUid) && Modes.TryToolState(args.EntityUid, out _)
            ? args.EntityUid
            : (EntityUid?) null;
        if ((clicked ?? Modes.ToolTargetAt(Modes.ToMap(args.Coordinates))) is not { } target)
        {
            Modes.Popup($"wf-mapping-tool-none-{Modes.Tool.ToString().ToLowerInvariant()}");
            return true;
        }

        var net = _entities.GetNetEntity(target);
        if (WFMappingBuildModeSystem.ToggleOf(Modes.Tool) is { } toggle)
            Modes.Send(new MappingToolsToggleEvent(net, toggle));
        else
            Modes.Send(new MappingToolsWrenchEvent(net));

        return true;
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        return true;
    }
}
