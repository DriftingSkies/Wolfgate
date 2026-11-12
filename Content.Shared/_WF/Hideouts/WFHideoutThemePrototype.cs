using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Hideouts;

/// <summary>
/// The materials a module is laid in: a survey stake set to a theme swaps the prefab's walls, windows, doors and floors
/// for the theme's as it lays them, for a fee.
/// </summary>
[Prototype("wfHideoutTheme")]
public sealed partial class WFHideoutThemePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>Where it sits in the stake's list, lowest first.</summary>
    [DataField]
    public int Order;

    /// <summary>What laying a module in the theme costs, on top of the module.</summary>
    [DataField]
    public int Price;

    /// <summary>The prefab entities it swaps, and what for.</summary>
    [DataField]
    public Dictionary<EntProtoId, EntProtoId> Entities = new();

    /// <summary>The prefab floor tiles it swaps, and what for.</summary>
    [DataField]
    public Dictionary<ProtoId<ContentTileDefinition>, ProtoId<ContentTileDefinition>> Tiles = new();
}
