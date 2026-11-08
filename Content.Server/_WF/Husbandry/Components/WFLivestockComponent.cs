namespace Content.Server._WF.Husbandry.Components;

/// <summary>An animal kept as livestock: passive, and saved with an outpost while it stands in a pen.</summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(WFHusbandrySystem))]
public sealed partial class WFLivestockComponent : Component
{
    /// <summary>How old the animal was at <see cref="Since"/>.</summary>
    [DataField]
    public TimeSpan Age;

    /// <summary>When the animal was last given its <see cref="Age"/>.</summary>
    [DataField, AutoPausedField]
    public TimeSpan Since;

    /// <summary>Whether it is out of a pen in an Active window, and so in the prey faction.</summary>
    [ViewVariables]
    public bool Exposed;
}
