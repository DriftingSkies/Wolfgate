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

    /// <summary>The number of the outpost lot it last stood on, from 1, which it goes back to; 0 with none.</summary>
    [DataField]
    public int Lot;
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

    /// <summary>How well things grow in the world's weather now, from 0.4 to 1; what its farms' rates are scaled by.</summary>
    [ViewVariables]
    public float Climate = 1f;

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

    /// <summary>The supply run on its way to the colony or holding over it, an encounter.</summary>
    [ViewVariables]
    public EntityUid? Run;

    /// <summary>When the next supply run may set out.</summary>
    [ViewVariables]
    public TimeSpan NextRun;
}

/// <summary>A supply run to a colony, on its encounter: what it carries and where it holds over the world.</summary>
[RegisterComponent, Access(typeof(WFColonySystem))]
public sealed partial class WFColonySupplyRunComponent : Component
{
    [ViewVariables]
    public string World = string.Empty;

    /// <summary>The key of its freighter in the encounter.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    /// <summary>The point over the world, past its gravity well, where it holds and sends its pods down.</summary>
    [ViewVariables]
    public System.Numerics.Vector2 Station;

    /// <summary>Crates of what the colony is owed still aboard.</summary>
    [ViewVariables]
    public float Hold;

    [ViewVariables]
    public bool Arrived;

    [ViewVariables]
    public TimeSpan NextPod;
}

/// <summary>A colony's seat this round, on its world's ground: which one stands and where, and who lives at it.</summary>
[RegisterComponent, UnsavedComponent, Access(typeof(WFColonySystem))]
public sealed partial class WFColonySeatComponent : Component
{
    [ViewVariables]
    public Robust.Shared.Prototypes.ProtoId<Content.Shared._WF.Colonies.WFColonySeatPrototype> Seat;

    /// <summary>The site's tiles on the ground, upper edges exclusive.</summary>
    [ViewVariables]
    public Box2i Bounds;

    /// <summary>The colonists living at the seat.</summary>
    [ViewVariables]
    public List<EntityUid> Colonists = new();
}

/// <summary>A vendor or chute mapped into a colony's depot: it trades on its world's colony shelf while one stands.</summary>
[RegisterComponent]
public sealed partial class WFColonyDepotMachineComponent : Component;

/// <summary>The beacon at a governor's hall: its light takes the governing company's colour.</summary>
[RegisterComponent]
public sealed partial class WFColonyHallBeaconComponent : Component
{
    /// <summary>Its colour while the world has no governor.</summary>
    [DataField]
    public Color Unchartered = Color.FromHex("#8FBF7F");
}

/// <summary>The charter office terminal in a governor's hall.</summary>
[RegisterComponent]
public sealed partial class WFColonyOfficeComponent : Component;

/// <summary>A colony exchange chute: it also buys farm produce for the shelf (<c>wfColonyProduce</c>).</summary>
[RegisterComponent]
public sealed partial class WFColonyProduceChuteComponent : Component;

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

/// <summary>A build standing in a yard's berth as it goes up: round state, made again each round from its build's progress.</summary>
[RegisterComponent, UnsavedComponent, Access(typeof(WFColonyYardSystem))]
public sealed partial class WFColonyYardFrameComponent : Component
{
    [ViewVariables]
    public string World = string.Empty;
}

/// <summary>A packed yard platform the governor deploys in its world's orbit.</summary>
[RegisterComponent, Access(typeof(WFColonyYardSystem))]
public sealed partial class WFColonyYardKitComponent : Component;

/// <summary>A crate chute on a yard platform that feeds the builds on its berths.</summary>
[RegisterComponent, Access(typeof(WFColonyYardSystem))]
public sealed partial class WFColonyYardChuteComponent : Component;
