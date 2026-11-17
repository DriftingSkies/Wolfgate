namespace Content.Server._WG.Wolfmed.Life;

/// <summary>A wound host's exposure to hazardous low pressure with no pressure suit, read once a second.</summary>
[RegisterComponent, Access(typeof(WolfmedVacuumSystem))]
public sealed partial class WolfmedVacuumComponent : Component
{
    /// <summary>The pressure the body feels is at or under the low pressure hazard line right now.</summary>
    [ViewVariables]
    public bool Exposed;

    /// <summary>When the body may next be told it is exposed, so standing in a doorway does not repeat the line.</summary>
    [ViewVariables]
    public TimeSpan NextTell;
}
