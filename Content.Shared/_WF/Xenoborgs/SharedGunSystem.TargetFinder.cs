using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

/// <summary>
/// wizden's target finder: a <see cref="TargetFinderHitscanComponent"/> ray marks what it hits as the target of its
/// <see cref="TargetFinderComponent"/> gun, which hands it to the linked <see cref="TargetAssignComponent"/> gun.
/// </summary>
public abstract partial class SharedGunSystem
{
    [Dependency] private SharedDeviceLinkSystem _wfDeviceLink = default!;

    [SubscribeLocalEvent]
    private void OnTargetFinderHitscanHit(Entity<TargetFinderHitscanComponent> entity, ref HitscanRaycastFiredEvent args)
    {
        if (!TryComp<TargetFinderComponent>(args.Gun, out var finder))
            return;

        foreach (var hit in args.HitEntities)
        {
            UpdateTarget((args.Gun, finder), hit);
            return;
        }
    }

    [SubscribeLocalEvent]
    private void OnTargetFinderMapInit(Entity<TargetFinderComponent> ent, ref MapInitEvent args)
    {
        foreach (var sink in _wfDeviceLink.GetLinkedSinks(ent.Owner, ent.Comp.LinkingPort))
        {
            if (!HasComp<TargetAssignComponent>(sink))
                continue;

            ent.Comp.TargetAssigner = sink;
            break;
        }
    }

    [SubscribeLocalEvent]
    private void OnTargetFinderNewLink(EntityUid uid, TargetFinderComponent comp, NewLinkEvent args)
    {
        if (args.Source != uid || args.SourcePort != comp.LinkingPort || !HasComp<TargetAssignComponent>(args.Sink))
            return;

        comp.TargetAssigner = args.Sink;
    }

    [SubscribeLocalEvent]
    private void OnTargetFinderPortDisconnected(EntityUid uid, TargetFinderComponent comp, PortDisconnectedEvent args)
    {
        if (args.Port != comp.LinkingPort || comp.TargetAssigner == null)
            return;

        comp.TargetAssigner = null;
    }

    private void UpdateTarget(Entity<TargetFinderComponent> ent, EntityUid target)
    {
        ent.Comp.Target = target;

        if (TryComp<TargetAssignComponent>(ent.Comp.TargetAssigner, out var targetAssign))
            targetAssign.Target = target;
    }
}
