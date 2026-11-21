using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Fishing;

/// <summary>What bites in some water: a world's open water by its outside temperature, or a pond built on an outpost.</summary>
[Prototype("wgFishTable")]
public sealed partial class WGFishTablePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Outside temperature band in Celsius.</summary>
    [DataField]
    public float MinTemp = -100f;

    [DataField]
    public float MaxTemp = 100f;

    /// <summary>Whether it stocks ponds instead of open water.</summary>
    [DataField]
    public bool Pond;

    [DataField(required: true)]
    public List<WGFishEntry> Fish = new();
}

[DataDefinition]
public sealed partial class WGFishEntry
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public float Weight = 1f;
}

/// <summary>What else a fish table gives: fish that bite only at night or in the rain, and its one legendary fish.</summary>
[Prototype("wgFishExtras")]
public sealed partial class WGFishExtrasPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<WGFishTablePrototype> Table;

    /// <summary>Added to the table's fish between dusk and dawn on a world with a day.</summary>
    [DataField]
    public List<WGFishEntry> Night = new();

    /// <summary>Added to the table's fish while it rains.</summary>
    [DataField]
    public List<WGFishEntry> Rain = new();

    [DataField]
    public EntProtoId? Legendary;
}

/// <summary>The odds of every catch that isn't an ordinary fish: junk, a legendary fish and the rare treasure chest.</summary>
[Prototype("wgFishLoot")]
public sealed partial class WGFishLootPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Chance a catch is junk instead of a fish; bait lowers it.</summary>
    [DataField]
    public float JunkChance = 0.06f;

    [DataField]
    public List<WGFishEntry> Junk = new();

    /// <summary>Chance a chest comes up with the catch, besides the fish.</summary>
    [DataField]
    public float TreasureChance = 0.01f;

    [DataField]
    public EntProtoId? Treasure;

    /// <summary>Chance the catch is the water's legendary fish; bait and a better rod raise it.</summary>
    [DataField]
    public float LegendaryChance = 0.005f;

    /// <summary>The shellfish a crab cage catches: those of the water's table, or any of these if it has none.</summary>
    [DataField]
    public List<WGFishEntry> CageFallback = new();
}
