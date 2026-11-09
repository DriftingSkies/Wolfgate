using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Tag;
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

    /// <summary>Whether it grows by the weather: its rate follows the world's outside temperature.</summary>
    [DataField]
    public bool Climate;
}

/// <summary>
/// A kind of farm produce the colony exchange's chute buys: an item of it, or a reagent poured in from a container. It
/// goes on the shelf as its worth in crates of <see cref="Good"/>.
/// </summary>
[Prototype("wfColonyProduce")]
public sealed partial class WFColonyProducePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The trade good it becomes on the shelf.</summary>
    [DataField(required: true)]
    public EntProtoId Good;

    /// <summary>Entity prototypes that are this produce.</summary>
    [DataField]
    public List<EntProtoId> Prototypes = new();

    /// <summary>Tags, any of which marks an item as this produce.</summary>
    [DataField]
    public List<ProtoId<TagPrototype>> Tags = new();

    /// <summary>Component names, any of which marks an item as this produce.</summary>
    [DataField]
    public List<string> Components = new();

    /// <summary>Reagents that are this produce, taken out of the container they are sold in.</summary>
    [DataField]
    public List<ProtoId<ReagentPrototype>> Reagents = new();

    /// <summary>Thalers a unit of those reagents is worth.</summary>
    [DataField]
    public float ReagentValue = 1f;

    /// <summary>Thalers one item is worth at least, for produce that appraises at less; a stack counts each of its items.</summary>
    [DataField]
    public float Value;
}

/// <summary>
/// A colony's seat: the site a world of at least <see cref="Tier"/> gets among its points of interest as the round's
/// worlds are built. A world gets the one of the highest tier it has reached.
/// </summary>
[Prototype("wfColonySeat")]
public sealed partial class WFColonySeatPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public WFWorldTier Tier;

    /// <summary>The site, placed before the world's own.</summary>
    [DataField(required: true)]
    public WFPlanetPoiEntry Site = default!;

    /// <summary>People its bunks house, on top of the outposts'.</summary>
    [DataField]
    public int Housing;

    /// <summary>Most colonists who live at it.</summary>
    [DataField]
    public int Colonists;
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

    /// <summary>The tier the world never reads below, such as Settled for one with a city standing on it.</summary>
    [DataField]
    public WFWorldTier BaseTier = WFWorldTier.None;

    /// <summary>What the world does to a building kind's rate there; a kind not listed runs at its own rate.</summary>
    [DataField]
    public Dictionary<ProtoId<WFColonyBuildingPrototype>, float> Rates = new();
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
