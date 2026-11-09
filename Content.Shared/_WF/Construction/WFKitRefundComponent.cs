using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Construction;

/// <summary>The kits installed in a machine, given back when it is taken apart.</summary>
[RegisterComponent]
public sealed partial class WFKitRefundComponent : Component
{
    [DataField]
    public List<EntProtoId> Kits = new();
}
