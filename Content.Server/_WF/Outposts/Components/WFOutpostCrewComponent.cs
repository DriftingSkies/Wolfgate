using Content.Shared._WF.RoundSetup;

namespace Content.Server._WF.Outposts.Components;

/// <summary>
/// The crew an outpost keeps through its saves: its Crew Setup name, description, admission and openings, written by
/// RoundSetup before each save and taken back when the outpost's crew forms again. Members and codes aren't kept.
/// </summary>
[RegisterComponent]
public sealed partial class WFOutpostCrewComponent : Component
{
    [DataField]
    public string Title = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public WFLobbyAccess Access = WFLobbyAccess.Requests;

    [DataField]
    public bool AllowLateJoin = true;

    [DataField]
    public List<WFOutpostCrewRole> Roles = new();
}

/// <summary>A saved crew opening: its title, job and slots.</summary>
[DataDefinition]
public sealed partial class WFOutpostCrewRole
{
    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Job = string.Empty;

    [DataField]
    public int Capacity = 1;
}
