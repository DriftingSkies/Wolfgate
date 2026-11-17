using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Slow;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCSlowdownComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt;
}
