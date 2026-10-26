using Content.Shared._Onyx.Wounds;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>Restores health to a wound host's eyes while they still work. Destroyed eyes stay destroyed.</summary>
public sealed partial class WolfmedMendEyes : EntityEffect
{
    /// <summary>Eyes health restored per metabolism tick, before reagent scaling.</summary>
    [DataField] public FixedPoint2 Amount = 0.25;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-mend-eyes", ("chance", Probability), ("amount", Amount.Float()));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.HasComponent<WoundHostComponent>(args.TargetEntity))
            return;

        var scale = args is EntityEffectReagentArgs reagent ? reagent.Scale : FixedPoint2.New(1);
        var mend = new WolfmedMendEyesEvent(Amount * scale);
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, ref mend);
    }
}

/// <summary>Raised on a wound host to restore this much health to its eyes.</summary>
[ByRefEvent]
public readonly record struct WolfmedMendEyesEvent(FixedPoint2 Amount);
