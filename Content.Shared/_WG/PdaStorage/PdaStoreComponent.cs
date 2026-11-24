using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.PdaStorage;

/// <summary>
/// Marks a cartridge program as the app store. The server system keys its messages off this.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PdaStoreCartridgeComponent : Component
{
}

/// <summary>
/// Puts the app store on a PDA at map init, the same pattern as the GameHub installer.
/// </summary>
[RegisterComponent]
public sealed partial class PdaAppStoreInstallerComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Program;
}
