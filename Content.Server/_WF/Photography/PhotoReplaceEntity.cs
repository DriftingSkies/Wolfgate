using Content.Shared.EntityEffects;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Photography;

/// <summary>Turns the target into another entity, back into the hand that held it.</summary>
public sealed partial class PhotoReplaceEntity : EntityEffect
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return null;
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entMan = args.EntityManager;
        var target = args.TargetEntity;
        var containers = entMan.System<SharedContainerSystem>();
        var transform = entMan.System<SharedTransformSystem>();

        var holder = containers.TryGetContainingContainer((target, null, null), out var container)
            ? container.Owner
            : (EntityUid?) null;

        var replacement = entMan.SpawnAtPosition(Prototype, transform.GetMoverCoordinates(target));
        entMan.QueueDeleteEntity(target);

        if (holder != null)
            entMan.System<SharedHandsSystem>().PickupOrDrop(holder.Value, replacement);
    }
}
