using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Planets;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Colonies;

/// <summary>
/// A kind of building that works a colony's ledger: every entity on a holder's outposts with all of its components
/// adds its rate to an industry, or makes its good outright.
/// </summary>
[Prototype("wfColonyBuilding")]
public sealed partial class WFColonyBuildingPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Component names an entity needs, all of them, to count.</summary>
    [DataField(required: true)]
    public List<string> Components = new();

    /// <summary>The ledger industry it adds to, or null when it makes <see cref="Good"/> outright.</summary>
    [DataField]
    public ProtoId<WFTradeIndustryPrototype>? Industry;

    /// <summary>The good it makes outright, with no input.</summary>
    [DataField]
    public EntProtoId? Good;

    /// <summary>Crate-equivalents an hour each one adds.</summary>
    [DataField]
    public float Rate = 1f;

    /// <summary>Most of them a holder counts on one world.</summary>
    [DataField]
    public int Most = 8;

    /// <summary>Whether it only counts while powered.</summary>
    [DataField]
    public bool Powered;

    /// <summary>The deposit kind it must stand on, for extractors.</summary>
    [DataField]
    public string? Deposit;

    /// <summary>
    /// Whether it makes its industry's output out of that industry's materials in its own storage: it counts only
    /// while it holds some, and what it makes draws them down.
    /// </summary>
    [DataField]
    public bool WorksMaterials;
}

/// <summary>What a world gives colonies: the deposits placed on it each round.</summary>
[Prototype("wfColonyWorld")]
public sealed partial class WFColonyWorldPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<WFPlanetSurfacePrototype> Surface;

    [DataField]
    public List<WFColonyDepositEntry> Deposits = new();
}

/// <summary>A deposit entity and how many of it a world gets.</summary>
[DataDefinition]
public sealed partial class WFColonyDepositEntry
{
    [DataField(required: true)]
    public EntProtoId Deposit;

    [DataField]
    public int Count = 3;
}

/// <summary>A hull only a colony yard builds, in stages fed with crates; its first build grants the company a licence.</summary>
[Prototype("wfYardDesign")]
public sealed partial class WFYardDesignPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<VesselPrototype> Vessel;

    [DataField(required: true)]
    public List<WFYardStage> Stages = new();
}

/// <summary>One stage of a yard build: the crates it eats and the work it takes once they are in.</summary>
[DataDefinition]
public sealed partial class WFYardStage
{
    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public int Components;

    [DataField]
    public int Metals;

    /// <summary>Minutes of powered work with one crane; more cranes share it.</summary>
    [DataField]
    public float Minutes = 20f;
}
