using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Construction;

/// <summary>The kits installed in a machine, given back when it is taken apart.</summary>
[RegisterComponent]
public sealed partial class WGKitRefundComponent : Component
{
    [DataField]
    public List<EntProtoId> Kits = new();
}
