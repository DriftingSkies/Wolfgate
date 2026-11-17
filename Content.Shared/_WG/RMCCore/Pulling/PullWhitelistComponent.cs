using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Pulling;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCPullingSystem))]
public sealed partial class PullWhitelistComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public EntityWhitelist Whitelist = new();
}
