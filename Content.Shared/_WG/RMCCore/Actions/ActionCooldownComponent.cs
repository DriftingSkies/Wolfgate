using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Actions;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedRMCActionsSystem))]
public sealed partial class ActionCooldownComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan Cooldown;
}
