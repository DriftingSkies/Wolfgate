using Robust.Shared.GameObjects;

namespace Content.Client._WF.StateRecovery;

/// <summary>
/// Removes a PVS-detached entity from its old lookup tree when the engine skips it.
/// </summary>
/// <remarks>
/// When a child leaves view in the same batch as its parent and is handled first, the engine leaves its removal to the
/// parent's recursive removal, but the child is already in null-space by then. Its entry stays in the grid's sundries
/// tree after it is deleted, and every lookup there logs an unresolvable transform.
/// </remarks>
public sealed class WFDetachLookupSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntParentChangedMessage>(OnParentChanged);
    }

    private void OnParentChanged(ref EntParentChangedMessage args)
    {
        if (args.Transform.ParentUid.IsValid() ||
            args.OldParent is not { } oldParent ||
            !oldParent.IsValid() ||
            (MetaData(args.Entity).Flags & MetaDataFlags.Detached) == 0 ||
            !TryComp(oldParent, out TransformComponent? parentXform) ||
            !_lookup.TryFindBroadphase(parentXform, out var broadphase))
        {
            return;
        }

        broadphase.SundriesTree.Remove(args.Entity);
        broadphase.StaticSundriesTree.Remove(args.Entity);
    }
}
