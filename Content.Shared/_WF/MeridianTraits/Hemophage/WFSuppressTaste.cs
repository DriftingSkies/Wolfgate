using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.MeridianTraits.Hemophage;

/// <summary>Dulls taste for a short while each metabolism tick, so a hemophage's tumor ignores what goes down.</summary>
public sealed partial class WFSuppressTaste : EntityEffect
{
    /// <summary>How long each tick keeps taste dulled.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(3);

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("wf-reagent-effect-guidebook-suppress-taste", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var ev = new WFSuppressTasteEvent(Duration);
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, ref ev);
    }
}
