using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Hideouts;

/// <summary>The hideout's own numbers, in one prototype so they are tuned in YAML.</summary>
[Prototype("wfHideoutRules")]
public sealed partial class WFHideoutRulesPrototype : IPrototype
{
    public const string Default = "WFHideoutRules";

    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// What an outpost with a module built costs to land each round, as a share of what its parts are worth, in place of
    /// the outpost load markup: the upkeep of a home.
    /// </summary>
    [DataField]
    public float HomeMarkup = 0.1f;

    /// <summary>What moving an outpost's home world costs.</summary>
    [DataField]
    public int MoveFee = 250000;

    /// <summary>What founding an outpost from the registry at Central costs, in credits only.</summary>
    [DataField]
    public int FoundPrice = 15000;

    /// <summary>What the registry's drop down to a standing outpost costs.</summary>
    [DataField]
    public int RideFee = 2500;
}
