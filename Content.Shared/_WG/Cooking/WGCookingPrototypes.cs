using Content.Shared.Chemistry.Reagent;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Cooking;

/// <summary>How a hearth cooks something: fried in a pan on the hob, or baked in the oven.</summary>
public enum WGCookMethod : byte
{
    Fry,
    Bake,
}

/// <summary>What frying or baking an item turns it into, after so many seconds of heat (after Roguetown's cooked/fried types).</summary>
[Prototype("wgCookRecipe")]
public sealed partial class WGCookRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public WGCookMethod Method;

    [DataField]
    public List<EntProtoId> Inputs = new();

    [DataField]
    public List<ProtoId<TagPrototype>> Tags = new();

    [DataField(required: true)]
    public EntProtoId Result;

    [DataField]
    public float Seconds = 30f;

    /// <summary>Higher is tried first, so a tough meat's chewy fry wins over the plain game-meat fry.</summary>
    [DataField]
    public int Priority;
}

/// <summary>A soup an ingredient makes in a boiling pot.</summary>
[Prototype("wgStewRecipe")]
public sealed partial class WGStewRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<EntProtoId> Inputs = new();

    [DataField]
    public List<ProtoId<TagPrototype>> Tags = new();

    /// <summary>For a stew of several ingredients: what has to be stewing in the pot together.</summary>
    [DataField]
    public List<WGPrepIngredient> Combine = new();

    /// <summary>The reagent the water becomes.</summary>
    [DataField(required: true)]
    public string Soup = string.Empty;

    [DataField]
    public float Seconds = 60f;

    [DataField]
    public int Order;
}

/// <summary>A drink an ingredient or a powder brews in a boiling kettle; only a kettle brews.</summary>
[Prototype("wgBrewRecipe")]
public sealed partial class WGBrewRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Items that brew it, dropped in whole.</summary>
    [DataField]
    public List<EntProtoId> Inputs = new();

    [DataField]
    public List<ProtoId<TagPrototype>> Tags = new();

    /// <summary>A powder that brews it, poured or spooned in.</summary>
    [DataField]
    public ProtoId<ReagentPrototype>? Powder;

    /// <summary>Units of drink each unit of powder makes from the water.</summary>
    [DataField]
    public float PowderYield = 5f;

    /// <summary>The reagent the water becomes.</summary>
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Drink;

    /// <summary>Seconds an item steeps.</summary>
    [DataField]
    public float Seconds = 15f;

    /// <summary>Higher is tried first, so berries brew berry tea before the fruit tea any fruit makes.</summary>
    [DataField]
    public int Priority;
}

/// <summary>What chopping an item on a prep board with a knife makes, as a potato into slices or a fish into fillets.</summary>
[Prototype("wgChopRecipe")]
public sealed partial class WGChopRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<EntProtoId> Inputs = new();

    [DataField]
    public List<ProtoId<TagPrototype>> Tags = new();

    [DataField(required: true)]
    public EntProtoId Result;

    [DataField]
    public int Count = 1;

    /// <summary>A lucky extra the cut now and then turns up, such as a pearl in an oyster.</summary>
    [DataField]
    public EntProtoId? Bonus;

    [DataField]
    public float BonusChance;
}

/// <summary>A raw dish put together on a prep board from its ingredients, ready for the oven or the pan.</summary>
[Prototype("wgPrepRecipe")]
public sealed partial class WGPrepRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<WGPrepIngredient> Ingredients = new();

    [DataField(required: true)]
    public EntProtoId Result;
}

[DataDefinition]
public sealed partial class WGPrepIngredient
{
    /// <summary>Any of these prototypes counts.</summary>
    [DataField]
    public List<EntProtoId> Ids = new();

    /// <summary>Or anything with this tag.</summary>
    [DataField]
    public ProtoId<TagPrototype>? Tag;

    [DataField]
    public int Count = 1;
}

/// <summary>A seasoning, used on a home-cooked dish: up to two to a dish, each once.</summary>
[Prototype("wgSeasoning")]
public sealed partial class WGSeasoningPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>What the dish is seasoned with, for examine.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>A reagent taken a unit at a time from the item used.</summary>
    [DataField]
    public string? Reagent;

    /// <summary>Or items used up whole.</summary>
    [DataField]
    public List<EntProtoId> Items = new();

    [DataField]
    public WGMealTrait? Trait;

    /// <summary>Strength it adds to the dish's buff.</summary>
    [DataField]
    public float Bonus = 0.1f;
}

/// <summary>What sits on a hearth's hob, for its sprite.</summary>
[Serializable, NetSerializable]
public enum WGHobVisuals : byte
{
    Cookware,

    /// <summary>Whether the hearth is burning.</summary>
    Lit,
}

[Serializable, NetSerializable]
public enum WGHobCookware : byte
{
    None,
    Pan,
    Pot,
}
