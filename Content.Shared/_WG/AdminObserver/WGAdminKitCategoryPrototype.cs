using Robust.Shared.Prototypes;

namespace Content.Shared._WG.AdminObserver;

/// <summary>
/// One group of the admin observer's kit catalogue. The Kit window lists these, <c>adminkit give</c> hands out only
/// what they list, and an admin without the Spawn flag has only these items saved in their layout.
/// </summary>
[Prototype("wgAdminKitCategory")]
public sealed partial class WGAdminKitCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The group's heading in the Kit window.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Where the group sorts; lower comes first.</summary>
    [DataField]
    public int Order;

    /// <summary>The items in the group, in display order.</summary>
    [DataField]
    public List<EntProtoId> Entries = new();
}
