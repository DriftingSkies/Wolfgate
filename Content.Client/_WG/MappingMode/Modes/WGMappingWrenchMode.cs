using Content.Shared._WG.MappingTools;
using Robust.Shared.Input.Binding;

namespace Content.Client._WG.MappingMode.Modes;

/// <summary>Tools: a left click uses the picked tool (wrench, bolts, weld, lock, panel, door) on what is under it.</summary>
public sealed partial class WGMappingWrenchMode(WGMappingBuildModeSystem modes) : WGMappingModeHandlerBase(modes)
{
    [Dependency] private IEntityManager _entities = default!;

    public override WGMappingBuildMode Mode => WGMappingBuildMode.Wrench;

    public override bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (!IsDown(args))
            return true;

        var clicked = _entities.EntityExists(args.EntityUid) && Modes.TryToolState(args.EntityUid, out _)
            ? args.EntityUid
            : (EntityUid?) null;
        if ((clicked ?? Modes.ToolTargetAt(Modes.ToMap(args.Coordinates))) is not { } target)
        {
            Modes.Popup($"wg-mapping-tool-none-{Modes.Tool.ToString().ToLowerInvariant()}");
            return true;
        }

        var net = _entities.GetNetEntity(target);
        if (WGMappingBuildModeSystem.ToggleOf(Modes.Tool) is { } toggle)
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
