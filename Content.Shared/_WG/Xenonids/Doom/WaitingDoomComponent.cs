using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Doom;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WaitingDoomComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan DoomAt;
}
