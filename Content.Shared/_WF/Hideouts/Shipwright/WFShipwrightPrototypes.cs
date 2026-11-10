using Content.Shared._Crescent.Hardpoints;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Hideouts.Shipwright;

/// <summary>Which share of a custom ship's refit points a system draws on, and what the console groups it under.</summary>
[Serializable, NetSerializable]
public enum WFShipwrightCategory : byte
{
    Defence,
    Industry,
    Utility,
    Mobility,
}

/// <summary>
/// A ship system the shipwright console fits: what it costs in refit points and thalers, what it installs, and which
/// components mark it on a ship, so a hand-built one is recognised too.
/// </summary>
[Prototype("wfShipwrightSystem")]
public sealed partial class WFShipwrightSystemPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    [DataField]
    public WFShipwrightCategory Category = WFShipwrightCategory.Utility;

    /// <summary>Refit points each one takes.</summary>
    [DataField(required: true)]
    public int Points;

    /// <summary>What fitting one costs, labour included.</summary>
    [DataField(required: true)]
    public int Price;

    /// <summary>What the cranes put on the ship; one that mounts on a hardpoint is fitted from the mounts, not the systems.</summary>
    [DataField(required: true)]
    public EntProtoId Install;

    /// <summary>Entity prototypes that are this system, checked before any system's components.</summary>
    [DataField]
    public List<EntProtoId> Prototypes = new();

    /// <summary>Component names that mark an entity as this system; the first system listing one of an entity's components claims it.</summary>
    [DataField]
    public List<string> Components = new();

    /// <summary>Most a ship may carry; 0 for no limit.</summary>
    [DataField]
    public int Limit;

    /// <summary>Where it sits in the console's list, lowest first.</summary>
    [DataField]
    public int Order;
}

/// <summary>A stock hull the shipwright cranes build from its keel: quicker to build and refit than a ship built by hand.</summary>
[Prototype("wfShipwrightFrame")]
public sealed partial class WFShipwrightFramePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>The hull, under SharedMaps so the console can preview it.</summary>
    [DataField(required: true)]
    public ResPath Grid;

    [DataField(required: true)]
    public int Price;

    /// <summary>The frame's refit points before the bay's levels add to them.</summary>
    [DataField(required: true)]
    public int Points;

    [DataField]
    public int Order;
}

/// <summary>The shipwright's numbers, in one prototype so they are tuned in YAML.</summary>
[Prototype("wfShipwrightRules")]
public sealed partial class WFShipwrightRulesPrototype : IPrototype
{
    public const string Default = "WFShipwrightRules";

    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Refit points a mounted gun takes, by the hardpoint size it needs.</summary>
    [DataField]
    public Dictionary<weaponSizes, int> WeaponPoints = new();

    /// <summary>Refit points a hardpoint itself takes, by its size; a frame's own hardpoints take none.</summary>
    [DataField]
    public Dictionary<weaponSizes, int> MountPoints = new();

    /// <summary>What laying a hardpoint costs, labour included, by its size.</summary>
    [DataField]
    public Dictionary<weaponSizes, int> MountPrices = new();

    /// <summary>Hardpoints any hull may carry, before its size adds more.</summary>
    [DataField]
    public int BaseMounts = 2;

    /// <summary>Floor tiles of a hull per extra hardpoint it may carry.</summary>
    [DataField]
    public int TilesPerMount = 12;

    /// <summary>What fitting a gun costs, as a share of its price.</summary>
    [DataField]
    public float WeaponMarkup = 1.5f;

    /// <summary>Thalers each crane job item costs on top of the part.</summary>
    [DataField]
    public int Labour = 250;

    /// <summary>What certifying a hand-built part costs, labour included.</summary>
    [DataField]
    public int CertifyPrice = 750;

    /// <summary>Refit points of a hand-built hull before its size and the bay add to them.</summary>
    [DataField]
    public int CustomPoints = 8;

    /// <summary>Floor tiles of a hand-built hull per refit point it gains from its size.</summary>
    [DataField]
    public int TilesPerPoint = 10;

    /// <summary>Most refit points a hand-built hull gains from its size.</summary>
    [DataField]
    public int SizePointsCap = 30;

    /// <summary>Most floor tiles a hand-built hull may have to register: the largest frame's.</summary>
    [DataField]
    public int CustomMaxTiles = 346;

    /// <summary>Work time of each crane item on a frame, against a repair's.</summary>
    [DataField]
    public float FrameWorkScale = 0.5f;

    /// <summary>Work time of each crane item on a hand-built hull, against a repair's.</summary>
    [DataField]
    public float CustomWorkScale = 1.5f;
}
