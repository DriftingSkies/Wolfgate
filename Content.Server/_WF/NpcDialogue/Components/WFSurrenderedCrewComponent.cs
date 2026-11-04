using Content.Shared._WF.NpcCrew;

namespace Content.Server._WF.NpcDialogue.Components;

/// <summary>On a crewman whose ship surrendered: what he was doing before, so a ship that fights again can put it back.</summary>
[RegisterComponent]
public sealed partial class WFSurrenderedCrewComponent : Component
{
    [ViewVariables]
    public WFCrewEngagement Engagement;

    [ViewVariables]
    public string Duty = string.Empty;

    /// <summary>Whether the surrender made him pacified, so taking it back undoes that and no more.</summary>
    [ViewVariables]
    public bool Pacified;
}
