using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.NpcCrew;

/// <summary>
/// A drink on an NPC bartender's menu: what it is called, the words that order it, and the reagents he pours into a
/// glass for it from the bar's dispensers. A mix is its ingredients; the reaction in the glass makes the drink.
/// </summary>
[Prototype("wgCrewDrink")]
public sealed partial class WGCrewDrinkPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>What the bartender calls it.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>A comma-separated list of the words and phrases that order it, matched as whole words.</summary>
    [DataField(required: true)]
    public LocId Keywords;

    /// <summary>The reagents poured, and how much of each, in units.</summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Pour = new();

    /// <summary>The reagent the glass holds once poured: the mix's product, or the one reagent of a plain drink.</summary>
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Serves;

    /// <summary>The glass it is served in.</summary>
    [DataField]
    public EntProtoId Glass = "DrinkGlass";
}

/// <summary>An NPC bartender pouring at a dispenser; what he pours is kept on him, which only the server reads.</summary>
[Serializable, NetSerializable]
public sealed partial class WGCrewBartenderDoAfterEvent : SimpleDoAfterEvent;
