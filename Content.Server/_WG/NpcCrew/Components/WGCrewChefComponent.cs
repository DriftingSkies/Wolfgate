using Content.Shared.Kitchen;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.NpcCrew.Components;

/// <summary>
/// A crew chef (duty Chef): keeps each dish on his menu stocked on the serving counter, cooking more in the galley's
/// own appliances from the ingredients aboard. Worked by <see cref="Systems.WGCrewChefSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class WGCrewChefComponent : Component
{
    /// <summary>The dishes he keeps out, each with how many he keeps on the counter.</summary>
    [DataField]
    public List<WGCrewChefDish> Menu = new();

    /// <summary>How far from his post, in metres, the galley reaches: its appliances, stores and serving spot markers.</summary>
    [DataField]
    public float KitchenRange = 10f;

    /// <summary>Without serving spot markers, how far from his post a table may be to count as the counter.</summary>
    [DataField]
    public float CounterRange = 3f;

    /// <summary>How often a pantry aboard gets one more dish's worth of ingredients while he is at work.</summary>
    [DataField]
    public TimeSpan RestockInterval = TimeSpan.FromMinutes(4);

    /// <summary>Sets of ingredients per dish a pantry is restocked up to.</summary>
    [DataField]
    public int PantrySets = 2;

    /// <summary>How close a player has to come for him to call out what's on.</summary>
    [DataField]
    public float GreetRange = 3f;

    [DataField]
    public TimeSpan GreetCooldown = TimeSpan.FromMinutes(3);

    /// <summary>How long a player he greeted waits before he is greeted again.</summary>
    [DataField]
    public TimeSpan GreetSamePlayer = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan ServeBarkCooldown = TimeSpan.FromMinutes(2);

    [ViewVariables]
    public TimeSpan NextCook;

    [ViewVariables]
    public TimeSpan NextRestock;

    [ViewVariables]
    public TimeSpan NextGreeting;

    [ViewVariables]
    public TimeSpan NextServeBark;

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Greeted = new();
}

/// <summary>One dish on a chef's menu.</summary>
[DataDefinition]
public sealed partial class WGCrewChefDish
{
    /// <summary>The recipe he cooks it by. Solid ingredients only; a recipe that needs reagents is skipped.</summary>
    [DataField(required: true)]
    public ProtoId<FoodRecipePrototype> Recipe;

    /// <summary>How many he keeps on the counter.</summary>
    [DataField]
    public int Stock = 2;
}
