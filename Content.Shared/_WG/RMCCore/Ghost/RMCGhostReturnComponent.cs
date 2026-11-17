using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Ghost;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCGhostReturnComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Target;
}
