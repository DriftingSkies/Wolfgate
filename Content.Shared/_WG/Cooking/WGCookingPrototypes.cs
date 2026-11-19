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

/// <summary>
/// A stew or soup: one ingredient added to a boiling pot turns some of its water into the soup after a while (after
/// Roguetown's stew recipes). Lower <see cref="Order"/> is tried first, so specific recipes win over generic ones.
/// </summary>
[Prototype("wgStewRecipe")]
public sealed partial class WGStewRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<EntProtoId> Inputs = new();

    [DataField]
    public List<ProtoId<TagPrototype>> Tags = new();

    /// <summary>
    /// For a stew of several ingredients: what has to be stewing in the pot together. When the pot holds them all they
    /// combine into this soup, one portion for each ingredient, in place of their separate soups.
    /// </summary>
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

/// <summary>What sits on a hearth's hob, for its sprite.</summary>
[Serializable, NetSerializable]
public enum WGHobVisuals : byte
{
    Cookware,
}

[Serializable, NetSerializable]
public enum WGHobCookware : byte
{
    None,
    Pan,
    Pot,
}
