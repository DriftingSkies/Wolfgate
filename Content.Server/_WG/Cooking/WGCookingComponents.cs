namespace Content.Server._WG.Cooking;

/// <summary>A hearth stove: a hob that holds one pan or pot over the fire, and an oven that bakes what is put in it.</summary>
[RegisterComponent, Access(typeof(WGCookingSystem))]
public sealed partial class WGHearthComponent : Component
{
    public const string Hob = "wg-hob";
    public const string Oven = "wg-oven";

    /// <summary>Whether it has a hob for a pan or pot; a converted microwave has only its oven.</summary>
    [DataField("hob")]
    public bool HasHob = true;

    /// <summary>How many things the oven holds; 0 for no oven.</summary>
    [DataField]
    public int OvenCapacity = 4;

    /// <summary>Seconds of heat a second gives; a hotter hearth cooks faster.</summary>
    [DataField]
    public float Heat = 1f;

    /// <summary>Whether it must be anchored to burn; a folding camp stove only needs to sit on the ground.</summary>
    [DataField]
    public bool NeedsAnchor = true;

    [ViewVariables]
    public float SizzleTimer;
}

/// <summary>An unbaked kitchen recipe (<c>microwaveMealRecipe</c>) waiting to be cooked.</summary>
[RegisterComponent, Access(typeof(WGCookingSystem))]
public sealed partial class WGRawDishComponent : Component
{
    [DataField]
    public string Recipe = string.Empty;
}

/// <summary>A frying pan: holds a few things to fry when it sits on a lit hob.</summary>
[RegisterComponent, Access(typeof(WGCookingSystem))]
public sealed partial class WGPanComponent : Component
{
    public const string Contents = "wg-pan";

    [DataField]
    public int Capacity = 3;
}

/// <summary>A cooking pot: water in it boils on a lit hob, and stew ingredients added to boiling water become soup.</summary>
[RegisterComponent, Access(typeof(WGCookingSystem))]
public sealed partial class WGPotComponent : Component
{
    [DataField]
    public string Solution = "pot";

    /// <summary>Seconds of heat the water has had; it boils at <see cref="BoilAt"/> and cools off the hob.</summary>
    [DataField]
    public float Boil;

    [DataField]
    public float BoilAt = 20f;

    /// <summary>Water each ingredient turns into soup.</summary>
    [DataField]
    public float Portion = 30f;

    [DataField]
    public List<WGStewJob> Jobs = new();

    /// <summary>How well it has been stirred since the last portion came out; 1 or more makes that portion bigger.</summary>
    [ViewVariables]
    public float Stirred;

    /// <summary>When it was last stirred; a stir counts once every <see cref="WGCookingSystem.StirEvery"/>.</summary>
    [ViewVariables]
    public TimeSpan LastStir;
}

/// <summary>An ingredient, or a combined set of them, stewing in a pot; saved with the pot.</summary>
[DataDefinition]
public sealed partial class WGStewJob
{
    [DataField]
    public string Recipe = string.Empty;

    [DataField]
    public float Left;

    /// <summary>The prototype that went in, for combining; empty once combined.</summary>
    [DataField]
    public string Ingredient = string.Empty;

    /// <summary>The combining tags the ingredient carried.</summary>
    [DataField]
    public HashSet<string> Tags = new();

    /// <summary>Portions of water it turns into soup.</summary>
    [DataField]
    public int Portions = 1;
}

/// <summary>A prep board: ingredients laid on it come together into a raw dish when they make one.</summary>
[RegisterComponent, Access(typeof(WGCookingSystem))]
public sealed partial class WGPrepBoardComponent : Component
{
    public const string Contents = "wg-board";

    [DataField]
    public int Capacity = 8;
}

/// <summary>Heat an item has taken in a pan or an oven; added as it cooks.</summary>
[RegisterComponent, Access(typeof(WGCookingSystem))]
public sealed partial class WGHeatingComponent : Component
{
    [DataField]
    public float Heat;

    /// <summary>It is a finished dish, so more heat only burns it.</summary>
    [DataField]
    public bool Done;

    [DataField]
    public bool Smoking;
}
