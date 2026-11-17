using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.CriticalGrace;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class InCriticalGraceComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan GraceEndsAt;

    [DataField, AutoNetworkedField]
    public bool Over = false;
}
