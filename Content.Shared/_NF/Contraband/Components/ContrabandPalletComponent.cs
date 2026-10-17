using Content.Shared.Stacks;
using Content.Shared._WF.EngineCompat;

namespace Content.Shared._NF.Contraband.Components;

[RegisterComponent]
[Access(typeof(SharedContrabandTurnInSystem))]
public sealed partial class ContrabandPalletConsoleComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField("cashType", serverOnly: true, customTypeSerializer:typeof(PrototypeIdSerializer<StackPrototype>))]
    public string RewardType = "FederationMilitaryCredit";

    [ViewVariables(VVAccess.ReadWrite), DataField(serverOnly: true)]
    public string Faction = "NFSD";

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public string LocStringPrefix = string.Empty;
}
