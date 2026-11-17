using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Traits.Effects;

/// <summary>
/// Spawns an item and puts it in the character's hand, or at their feet when they have no free hand.
/// </summary>
public sealed partial class SpawnItemInHandEffect : BaseTraitEffect
{
    [DataField(required: true)]
    public EntProtoId Item = string.Empty;

    public override void Apply(TraitEffectContext ctx)
    {
        var item = ctx.EntMan.SpawnEntity(Item, ctx.Transform.Coordinates);

        if (!ctx.EntMan.TryGetComponent<HandsComponent>(ctx.Player, out var hands))
            return;

        var handsSystem = ctx.EntMan.System<SharedHandsSystem>();
        handsSystem.TryPickup(ctx.Player, item, checkActionBlocker: false, handsComp: hands);
    }
}
