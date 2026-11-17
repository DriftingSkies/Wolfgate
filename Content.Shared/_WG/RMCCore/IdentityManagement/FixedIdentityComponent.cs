using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.IdentityManagement;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FixedIdentityComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public LocId? Name;

    [DataField, AutoNetworkedField]
    public EntityWhitelist? Whitelist;
}
