namespace Content.Client._WG.MappingMode.Modes;

/// <summary>Select: the mapping tools' selection, which takes the clicks itself; Escape drops the selection.</summary>
public sealed partial class WGMappingSelectMode(WGMappingBuildModeSystem modes) : WGMappingModeHandlerBase(modes)
{
    public override WGMappingBuildMode Mode => WGMappingBuildMode.Select;

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
