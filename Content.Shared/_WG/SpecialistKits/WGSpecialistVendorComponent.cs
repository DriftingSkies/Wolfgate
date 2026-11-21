using Content.Shared._Mono.Company;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.SpecialistKits;

/// <summary>On a specialist rack: whose faction it serves and how many of each limited kit that faction may still take.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WGSpecialistVendorComponent : Component
{
    [DataField(required: true)]
    public ProtoId<CompanyPrototype> Company;

    /// <summary>Slots left this round for each limited kit on the rack, kept current by the server.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<EntProtoId, int> Left = new();
}
