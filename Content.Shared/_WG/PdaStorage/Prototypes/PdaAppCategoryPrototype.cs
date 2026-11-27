using Robust.Shared.Prototypes;

namespace Content.Shared._WG.PdaStorage.Prototypes;

/// <summary>
/// One group in the app store. Order decides the tab order; apps reference it by id.
/// </summary>
[Prototype("pdaAppCategory")]
public sealed partial class PdaAppCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public int Order;
}
