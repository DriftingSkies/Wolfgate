using Content.Shared._Onyx.Wounds;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Wolfmed.EntityEffects;

/// <summary>Restores health to a wound host's brain while it still works. A dead brain stays dead.</summary>
public sealed partial class WolfmedMendBrain : EntityEffect
{
    /// <summary>Brain health restored per metabolism tick, before reagent scaling.</summary>
    [DataField] public FixedPoint2 Amount = 0.25;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-mend-brain", ("chance", Probability), ("amount", Amount.Float()));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.HasComponent<WoundHostComponent>(args.TargetEntity))
            return;

        var scale = args is EntityEffectReagentArgs reagent ? reagent.Scale : FixedPoint2.New(1);
        var mend = new WolfmedMendBrainEvent(Amount * scale);
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, ref mend);
    }
}

/// <summary>Raised on a wound host to restore this much health to its brain.</summary>
[ByRefEvent]
public readonly record struct WolfmedMendBrainEvent(FixedPoint2 Amount);
