using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.BritishFood;

/// <summary>Takes health off a wound host's heart, as too much saturated fat at once does.</summary>
public sealed partial class WGStrainHeart : EntityEffect
{
    /// <summary>Heart health taken per metabolism tick, before reagent scaling.</summary>
    [DataField]
    public FixedPoint2 Amount = 0.02;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("wg-reagent-effect-guidebook-strain-heart",
            ("chance", Probability),
            ("amount", Amount.Float()));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var scale = args is EntityEffectReagentArgs reagent ? reagent.Scale : FixedPoint2.New(1);
        var strain = new WGStrainHeartEvent(Amount * scale);
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, ref strain);
    }
}

/// <summary>Raised on an entity to take this much health off its heart.</summary>
[ByRefEvent]
public readonly record struct WGStrainHeartEvent(FixedPoint2 Amount);
