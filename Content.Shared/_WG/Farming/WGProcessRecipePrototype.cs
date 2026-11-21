using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Farming;

/// <summary>One thing a processing machine does, after Stardew's artisan machines: one item in, goods out after a time.</summary>
[Prototype("wgProcessRecipe")]
public sealed partial class WGProcessRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The machine kind that runs it, matching <c>WGProcessor.machine</c>.</summary>
    [DataField(required: true)]
    public string Machine = string.Empty;

    /// <summary>Items it takes by prototype.</summary>
    [DataField]
    public List<EntProtoId> Inputs = new();

    /// <summary>Items it takes by tag.</summary>
    [DataField]
    public List<ProtoId<TagPrototype>> Tags = new();

    /// <summary>Whether it takes any harvested produce.</summary>
    [DataField]
    public bool Produce;

    /// <summary>Whether it runs with nothing put in.</summary>
    [DataField]
    public bool Auto;

    /// <summary>Real minutes it takes while the machine may run.</summary>
    [DataField]
    public float Minutes = 30f;

    /// <summary>A supply used up each batch, such as a glass bottle for the keg; loaded into the machine by hand beforehand.</summary>
    [DataField]
    public EntProtoId? Needs;

    /// <summary>Whether it makes seed packets of the produce put in, as a seed maker; <see cref="Outputs"/> then sets only the count.</summary>
    [DataField]
    public bool Seeds;

    /// <summary>What comes out: one of these, picked by weight.</summary>
    [DataField(required: true)]
    public List<WGProcessOutput> Outputs = new();

    /// <summary>Whether the goods keep the quality of what went in.</summary>
    [DataField]
    public bool KeepQuality = true;

    /// <summary>Whether the goods come out one quality step better, as from a cask.</summary>
    [DataField]
    public bool QualityUp;

    /// <summary>How many matching items it takes before a batch starts, as a packing table fills a crate.</summary>
    [DataField]
    public int Batch = 1;

    /// <summary>Whether it runs outside the outpost producer cap and isn't booked against it, as packing goods already made.</summary>
    [DataField]
    public bool Uncapped;

    /// <summary>Planted plant holders it needs within 5 tiles to run, as a hive needs flowers.</summary>
    [DataField]
    public int NeedsPlants;
}

[DataDefinition]
public sealed partial class WGProcessOutput
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public int Count = 1;

    [DataField]
    public float Weight = 1f;
}

/// <summary>An order an outpost order board can post: so many of an item, or of anything with a tag, for a price each.</summary>
[Prototype("wgFarmOrder")]
public sealed partial class WGFarmOrderPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>What the buyer calls the goods on the board.</summary>
    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public List<EntProtoId> Items = new();

    [DataField]
    public List<ProtoId<TagPrototype>> Tags = new();

    /// <summary>Tags that keep an item off the order, such as a slice of a pie it wants whole.</summary>
    [DataField]
    public List<ProtoId<TagPrototype>> ExcludeTags = new();

    [DataField]
    public int MinCount = 5;

    [DataField]
    public int MaxCount = 15;

    /// <summary>Paid for each item delivered; better quality pays more.</summary>
    [DataField]
    public int Price = 50;

    /// <summary>Paid on top once the order is filled.</summary>
    [DataField]
    public int Bonus = 500;

    [DataField]
    public float Weight = 1f;
}
