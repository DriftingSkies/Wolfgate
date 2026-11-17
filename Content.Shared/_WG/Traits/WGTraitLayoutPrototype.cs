using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Traits;

/// <summary>
/// Editor layout for one trait category: its order in the list and the subgroups its traits sit under.
/// The ID matches the trait category ID.
/// </summary>
[Prototype("wgTraitLayout")]
public sealed partial class WGTraitLayoutPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Lower values are listed first.</summary>
    [DataField]
    public int Priority;

    /// <summary>Replaces the category's own name in the editor.</summary>
    [DataField]
    public LocId? Name;

    /// <summary>Subgroups in display order. Traits in none of them go under a trailing "Other" group.</summary>
    [DataField]
    public List<WGTraitGroup> Groups = new();
}

/// <summary>
/// A titled subgroup of traits inside a category.
/// </summary>
[DataDefinition]
public sealed partial class WGTraitGroup
{
    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public List<ProtoId<TraitPrototype>> Traits = new();
}
