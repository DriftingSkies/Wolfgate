using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Pulling;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AllowPullWhileDeadAndInfectedComponent : Component
{
    [DataField, AutoNetworkedField]
    public int InfectionStageThreshold = 1;
}
