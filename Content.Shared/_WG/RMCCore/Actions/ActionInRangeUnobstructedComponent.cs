using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Actions;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedRMCActionsSystem))]
public sealed partial class ActionInRangeUnobstructedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range;
}
