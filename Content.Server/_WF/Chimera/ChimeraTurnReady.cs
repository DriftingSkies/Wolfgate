using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Chimera;

/// <summary>
/// Passes unless the target is a body still on its <see cref="ChimeraTurnCooldownComponent"/> cooldown.
/// </summary>
[UsedImplicitly]
public sealed partial class ChimeraTurnReady : EntityEffectCondition
{
    public override bool Condition(EntityEffectBaseArgs args)
    {
        return args.EntityManager.System<DropOriginalBodyOnDeathSystem>().CanTurn(args.TargetEntity);
    }

    public override string GuidebookExplanation(IPrototypeManager prototype)
    {
        return Loc.GetString("wf-reagent-effect-condition-guidebook-chimera-turn-ready");
    }
}
