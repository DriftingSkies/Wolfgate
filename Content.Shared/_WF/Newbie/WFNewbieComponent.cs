using Content.Shared.StatusIcon;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Newbie;

/// <summary>
/// On the body a new player is controlling: draws the learner mark and adds the examine line.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFNewbieComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<SsdIconPrototype> Icon = "WFNewbieIcon";
}

/// <summary>
/// On a ship a new player owns or is piloting: tags its radar label and hail name.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFNewbieShipComponent : Component
{
    /// <summary>Whether the owner is new, kept while they're offline. Server only.</summary>
    public bool OwnerTagged;
}
