using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Cooking;

/// <summary>What a home-cooked meal does for whoever eats it. A meal carries up to three.</summary>
public enum WGMealTrait : byte
{
    /// <summary>A little faster on foot.</summary>
    Energising,

    /// <summary>Hunger comes back more slowly.</summary>
    Hearty,

    /// <summary>Slow healing of bruises and burns.</summary>
    Fresh,

    /// <summary>Holds off the cold.</summary>
    Warming,

    /// <summary>Holds off the heat.</summary>
    Cooling,

    /// <summary>Bleeding stops sooner.</summary>
    Fortifying,
}

/// <summary>
/// A soup's or a drink's meal buff, given as it is drunk: each metabolism tick refreshes it on the drinker, so a
/// bowl keeps it up for as long as it lasts and <see cref="Minutes"/> after.
/// </summary>
public sealed partial class WGMealBuff : EntityEffect
{
    [DataField(required: true)]
    public List<WGMealTrait> Traits = new();

    /// <summary>How strong, 1 for an ordinary home-cooked dish.</summary>
    [DataField]
    public float Strength = 1f;

    [DataField]
    public float Minutes = 10f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wg-meal-buff", ("traits", string.Join(", ", Traits)));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var ev = new WGMealBuffEvent(args.TargetEntity, Traits, Strength, Minutes);
        args.EntityManager.EventBus.RaiseEvent(EventSource.Local, ref ev);
    }
}

/// <summary>Raised broadcast to give <paramref name="Target"/> a meal buff.</summary>
[ByRefEvent]
public readonly record struct WGMealBuffEvent(EntityUid Target, List<WGMealTrait> Traits, float Strength, float Minutes);
