using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Crippling;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoCripplingStrikeSystem))]
public sealed partial class VictimCripplingStrikeDamageComponent : Component
{
    [DataField, AutoNetworkedField]
    public float DamageMult = 1;
}
