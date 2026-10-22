using Robust.Shared.Input.Binding;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>Pick: a left click eyedrops what is under it and goes back to Place with it.</summary>
public sealed partial class WFMappingPickMode(WFMappingBuildModeSystem modes) : WFMappingModeHandlerBase(modes)
{
    public override WFMappingBuildMode Mode => WFMappingBuildMode.Pick;

    public override void Enter()
    {
        Modes.SetToolsPicking(true);
    }

    public override void Exit()
    {
        Modes.SetToolsPicking(false);
    }

    public override bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (IsDown(args))
            Modes.Pick(args);

        return true;
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        return true;
    }
}
