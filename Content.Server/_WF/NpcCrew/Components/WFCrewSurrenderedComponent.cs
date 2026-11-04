namespace Content.Server._WF.NpcCrew.Components;

/// <summary>On a ship whose crew has surrendered: it raises no alerts and fights nobody until this is removed.</summary>
[RegisterComponent]
public sealed partial class WFCrewSurrenderedComponent : Component
{
    /// <summary>The crew that surrendered.</summary>
    [ViewVariables]
    public string Group = string.Empty;
}
