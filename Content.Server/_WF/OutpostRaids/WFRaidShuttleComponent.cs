namespace Content.Server._WF.OutpostRaids;

/// <summary>The shuttle a raid set down in outside an outpost's claim; it leaves at dawn.</summary>
[RegisterComponent, Access(typeof(WFRaidDirectorSystem))]
public sealed partial class WFRaidShuttleComponent : Component
{
    [ViewVariables]
    public int Raid;
}
