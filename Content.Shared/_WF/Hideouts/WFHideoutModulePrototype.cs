using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Hideouts;

/// <summary>A part of an outpost its owner buys level by level: each level costs money and time, and may need other modules first.</summary>
[Prototype("wfHideoutModule")]
public sealed partial class WFHideoutModulePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>Where it sits in the list, lowest first.</summary>
    [DataField]
    public int Order;

    /// <summary>Whether only a company's outpost builds it.</summary>
    [DataField]
    public bool Company;

    /// <summary>Its levels in order; the first is level 1.</summary>
    [DataField(required: true)]
    public List<WFHideoutModuleLevel> Levels = new();
}

/// <summary>One level of a module: what it costs and needs, and what the outpost gains from it.</summary>
[DataDefinition]
public sealed partial class WFHideoutModuleLevel
{
    [DataField(required: true)]
    public int Price;

    /// <summary>Materials the level uses up, by stack type, in sheets or lengths: what lies on the outpost counts.</summary>
    [DataField]
    public Dictionary<ProtoId<StackPrototype>, int> Stacks = new();

    /// <summary>Parts the level uses up, by entity prototype: stock parts, medkits, toolboxes.</summary>
    [DataField]
    public Dictionary<EntProtoId, int> Parts = new();

    /// <summary>How long it takes to build, in seconds.</summary>
    [DataField]
    public float BuildTime = 60f;

    /// <summary>Modules that have to stand at a level before this one starts.</summary>
    [DataField]
    public Dictionary<ProtoId<WFHideoutModulePrototype>, int> Requires = new();

    /// <summary>What the level gives, in a line for the console.</summary>
    [DataField]
    public LocId? Summary;

    /// <summary>A grid the owner lays onto the outpost with the survey stake the level hands out; under SharedMaps.</summary>
    [DataField]
    public ResPath? Prefab;

    /// <summary>The layouts the stake can be set to, the prefab among them; empty when there is only the prefab.</summary>
    [DataField]
    public List<WFHideoutLayout> Layouts = new();

    /// <summary>Things left at the console when the level is done.</summary>
    [DataField]
    public List<EntProtoId> Grants = new();

    /// <summary>Floor tiles added to the outpost's cap.</summary>
    [DataField]
    public int Tiles;

    /// <summary>Tiles added to how far across the outpost may spread.</summary>
    [DataField]
    public int Span;

    /// <summary>Objects added to the outpost's cap.</summary>
    [DataField]
    public int Entities;

    /// <summary>Cryopods added to the outpost's limit.</summary>
    [DataField]
    public int Pods;

    /// <summary>The widest the hangar pad may be set, in tiles; the largest among the built levels counts.</summary>
    [DataField]
    public int HangarSize;

    /// <summary>The widest the shipwright bay's pad may be set, in tiles; the largest among the built levels counts.</summary>
    [DataField]
    public int ShipwrightSize;

    /// <summary>Refit points added to the shipwright ship's budget.</summary>
    [DataField]
    public int ShipwrightPoints;

    /// <summary>The share of a ship's value an insurance policy on it costs; the lowest among the built levels counts.</summary>
    [DataField]
    public float? InsurancePremium;

    /// <summary>The share of the insured value a claim costs; the lowest among the built levels counts.</summary>
    [DataField]
    public float? InsuranceDeductible;
}

/// <summary>One of the layouts a module's stake can lay.</summary>
[DataDefinition]
public sealed partial class WFHideoutLayout
{
    [DataField(required: true)]
    public LocId Name;

    /// <summary>The layout's grid; under SharedMaps.</summary>
    [DataField(required: true)]
    public ResPath Prefab;
}
