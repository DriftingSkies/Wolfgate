namespace Content.Server._WF.Colonies;

/// <summary>What an outpost keeps of its colony through its saves: the company it earns for and its heritage.</summary>
[RegisterComponent, Access(typeof(WFColonySystem))]
public sealed partial class WFColonyMemberComponent : Component
{
    /// <summary>The player company its ship access was linked to, relinked when a save settles.</summary>
    [DataField]
    public int? CompanyId;

    /// <summary>Rounds someone was home long enough to count.</summary>
    [DataField]
    public int Heritage;

    /// <summary>The round that last counted, so a round counts once.</summary>
    [DataField]
    public int HeritageRound;

    /// <summary>Minutes someone was home this round.</summary>
    [ViewVariables]
    public float HomeMinutes;
}

/// <summary>A colony's round state, on its world's sector body: who makes what, its people and what it is owed.</summary>
[RegisterComponent, Access(typeof(WFColonySystem))]
public sealed partial class WFColonyComponent : Component
{
    [ViewVariables]
    public string World = string.Empty;

    /// <summary>The world's planet network.</summary>
    [ViewVariables]
    public EntityUid Network;

    [ViewVariables]
    public float People;

    /// <summary>People the colony's beds and cryopods house.</summary>
    [ViewVariables]
    public int Housing;

    /// <summary>Crates an hour each holder's buildings add to each industry.</summary>
    [ViewVariables]
    public Dictionary<string, Dictionary<string, float>> Industries = new();

    /// <summary>Crates an hour each holder's buildings make of each good outright.</summary>
    [ViewVariables]
    public Dictionary<string, Dictionary<string, float>> Goods = new();

    /// <summary>The buildings whose own stores back a holder's output of an industry, such as ore silos.</summary>
    [ViewVariables]
    public Dictionary<(string Industry, string Holder), List<EntityUid>> Stores = new();

    /// <summary>Spesos owed to each holder for what its buildings made, paid out in whole spesos.</summary>
    [ViewVariables]
    public Dictionary<string, double> Owed = new();

    /// <summary>Crates of each good each holder kept off the shelf, dropped to its outposts as whole crates.</summary>
    [ViewVariables]
    public Dictionary<string, Dictionary<string, float>> Kept = new();

    [ViewVariables]
    public TimeSpan NextImport;

    /// <summary>When the outposts' buildings are next counted and the holders paid.</summary>
    [ViewVariables]
    public TimeSpan NextScan;

    [ViewVariables]
    public bool Blockaded;
}

/// <summary>A deposit an extractor standing on it works for a colony.</summary>
[RegisterComponent]
public sealed partial class WFColonyDepositComponent : Component
{
    [DataField(required: true)]
    public string Kind = string.Empty;
}

/// <summary>An extractor: counts as a colony building while it stands on a deposit its building kind names.</summary>
[RegisterComponent]
public sealed partial class WFColonyExtractorComponent : Component;

/// <summary>A yard console: lays down builds and shows the berths' progress.</summary>
[RegisterComponent, Access(typeof(WFColonyYardSystem))]
public sealed partial class WFColonyYardComponent : Component
{
    /// <summary>The world whose yard it runs, set when its platform is laid or loaded.</summary>
    [ViewVariables]
    public string World = string.Empty;
}

/// <summary>A world's yard platform, held by its charter, not by an outpost.</summary>
[RegisterComponent, Access(typeof(WFColonyYardSystem))]
public sealed partial class WFColonyYardPlatformComponent : Component
{
    [ViewVariables]
    public string World = string.Empty;

    /// <summary>When the platform is next saved.</summary>
    [ViewVariables]
    public TimeSpan NextSave;
}

/// <summary>A packed yard platform the governor deploys in its world's orbit.</summary>
[RegisterComponent, Access(typeof(WFColonyYardSystem))]
public sealed partial class WFColonyYardKitComponent : Component;

/// <summary>A crate chute on a yard platform that feeds the builds on its berths.</summary>
[RegisterComponent, Access(typeof(WFColonyYardSystem))]
public sealed partial class WFColonyYardChuteComponent : Component;
