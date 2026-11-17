using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.GasToggle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoGasToggleComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsNeurotoxin = false;
}