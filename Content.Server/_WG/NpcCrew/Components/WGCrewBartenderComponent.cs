using Content.Shared._WG.NpcCrew;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.NpcCrew.Components;

/// <summary>
/// An NPC bartender (<c>WGCrewBartenderSystem</c>): takes drink orders said near him or picked from his verbs, pours
/// each from the bar's dispensers into a glass from his rack and sets it on the counter in front of the customer.
/// Runtime state is not saved.
/// </summary>
[RegisterComponent]
public sealed partial class WGCrewBartenderComponent : Component
{
    /// <summary>What he pours, in the order he lists it.</summary>
    [DataField(required: true)]
    public List<ProtoId<WGCrewDrinkPrototype>> Menu = new();

    /// <summary>Orders he takes at once, the one he is pouring included.</summary>
    [DataField]
    public int MaxQueue = 8;

    /// <summary>Orders one customer may have waiting.</summary>
    [DataField]
    public int MaxPerCustomer = 2;

    /// <summary>How long he stands at a dispenser to pour.</summary>
    [DataField]
    public TimeSpan PourTime = TimeSpan.FromSeconds(2);

    /// <summary>How long an order may wait, start to finish, before he gives it up.</summary>
    [DataField]
    public TimeSpan OrderTimeout = TimeSpan.FromMinutes(3);

    /// <summary>How long one step of an order (a walk to a dispenser or the counter) may take before he gives it up.</summary>
    [DataField]
    public TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How far from his post a dispenser may stand and still be his.</summary>
    [DataField]
    public float SourceRange = 10f;

    /// <summary>How far from his post an empty glass on a counter counts as left on his bar.</summary>
    [DataField]
    public float CollectRange = 4f;

    /// <summary>How far from his post a customer may be and still be served where they stand.</summary>
    [DataField]
    public float ServeRange = 8f;

    /// <summary>How near him a customer stands to be at the counter, for a request that doesn't name him.</summary>
    [DataField]
    public float CounterRange = 2.5f;

    /// <summary>A message this short that names a drink is an order even without "please" or "can I get".</summary>
    [DataField]
    public int CasualWords = 5;

    /// <summary>Clean glasses on his rack: one goes out with each drink, and each empty he collects comes back.</summary>
    [DataField]
    public int Glasses = 12;

    [DataField]
    public int MaxGlasses = 12;

    /// <summary>How often a glass comes back from the wash on its own while the rack is short.</summary>
    [DataField]
    public TimeSpan RestockTime = TimeSpan.FromMinutes(2);

    /// <summary>Gap between his answers that aren't orders (the menu, an unknown drink), so a chatty bar isn't flooded.</summary>
    [DataField]
    public TimeSpan ReplyGap = TimeSpan.FromSeconds(3);

    /// <summary>Orders waiting, first come first served.</summary>
    [ViewVariables]
    public List<WGCrewDrinkOrder> Queue = new();

    /// <summary>The order being poured or carried.</summary>
    [ViewVariables]
    public WGCrewDrinkOrder? Current;

    /// <summary>The glass for <see cref="Current"/>, once taken from the rack.</summary>
    [ViewVariables]
    public EntityUid? Glass;

    /// <summary>The reagents of <see cref="Current"/> already in the glass.</summary>
    [ViewVariables]
    public HashSet<string> Poured = new();

    /// <summary>The dispenser he is pouring at, and the do-after doing it.</summary>
    [ViewVariables]
    public EntityUid? PouringAt;

    [ViewVariables]
    public DoAfterId? Pouring;

    /// <summary>When the step under way started, for <see cref="StepTimeout"/>.</summary>
    [ViewVariables]
    public TimeSpan StepSince;

    /// <summary>An empty glass on the bar he is going to collect.</summary>
    [ViewVariables]
    public EntityUid? Collect;

    [ViewVariables]
    public TimeSpan NextCollectScan;

    [ViewVariables]
    public TimeSpan NextRestock;

    [ViewVariables]
    public TimeSpan NextReply;
}

/// <summary>One drink ordered by one customer.</summary>
public sealed class WGCrewDrinkOrder
{
    public EntityUid Customer;
    public ProtoId<WGCrewDrinkPrototype> Drink;
    public TimeSpan Placed;
}
