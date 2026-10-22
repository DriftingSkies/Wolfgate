namespace Content.Client._WF.MappingMode.Modes;

/// <summary>Select: the mapping tools' selection, which takes the clicks itself; Escape drops the selection.</summary>
public sealed partial class WFMappingSelectMode(WFMappingBuildModeSystem modes) : WFMappingModeHandlerBase(modes)
{
    public override WFMappingBuildMode Mode => WFMappingBuildMode.Select;

    public override void Enter()
    {
        Modes.SetToolsSelecting(true);
    }

    public override void Exit()
    {
        Modes.SetToolsSelecting(false);
    }

    public override bool Cancel()
    {
        return Modes.ClearToolsSelection();
    }
}
