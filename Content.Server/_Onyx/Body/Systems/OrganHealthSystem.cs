using Content.Server.Body.Components; // WOLFGATE(Wolfmed): D13, Wolfgate's BrainComponent is server-only.
using Content.Shared.Body.Organ; // WOLFGATE(Wolfmed): Wolfgate keeps OrganComponent in Content.Shared.Body.Organ.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared._Onyx.Body;
using Content.Shared._WG.Wolfmed.Body; // WOLFGATE(Wolfmed): D8, organ health lives on WolfmedOrganComponent.
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Mobs;
using Robust.Shared.Network;
using Robust.Shared.Timing; // WOLFGATE(Performance)

namespace Content.Shared._Onyx.Body
{
    /// <summary>
    /// Raised after an organ crosses the functional health threshold.
    /// </summary>
    // WOLFGATE(Wolfmed): relocated from _Onyx/Body/FunctionalOrganComponent.cs; that file is Nubody glue and is not ported (D8).
    [ByRefEvent]
    public readonly record struct OrganFunctionChangedEvent(EntityUid Body, bool Functional);
}

namespace Content.Shared._Onyx.Body.Systems
{
    public sealed partial class OrganHealthSystem : EntitySystem
    {
        [Dependency] private INetManager _net = default!;
        [Dependency] private SharedBodySystem _body = default!;
        [Dependency] private WoundSystem _wounds = default!;
        [Dependency] private MobStateSystem _mobState = default!;
        [Dependency] private Content.Server._WG.Wolfmed.Body.WolfmedOrganFailureSystem _wgFailure = default!; // WOLFGATE(Wolfmed): revival pass
        // WOLFGATE(Performance) START: the failed-organ scan runs four times a second, not every tick
        [Dependency] private IGameTiming _wgTiming = default!;
        private static readonly TimeSpan WGScanInterval = TimeSpan.FromSeconds(0.25);
        private TimeSpan _wgNextScan;
        // WOLFGATE END

        public override void Update(float frameTime)
        {
            if (!_net.IsServer)
                return;

            // WOLFGATE(Performance) START: the failed-organ scan runs four times a second, not every tick
            if (_wgTiming.CurTime < _wgNextScan)
                return;
            _wgNextScan = _wgTiming.CurTime + WGScanInterval;
            // WOLFGATE END

            // WOLFGATE(Wolfmed): organ health is on WolfmedOrganComponent, so the query pairs it with Wolfgate's OrganComponent.
            var query = EntityQueryEnumerator<WolfmedOrganComponent, OrganComponent>();
            while (query.MoveNext(out var uid, out var organ, out var slotted))
            {
                if (organ.Health > FixedPoint2.Zero)
                    continue;

                if (HasComp<BrainComponent>(uid))
                {
                    if (slotted.Body is { } body &&
                        TryComp(body, out MobStateComponent? mobState) &&
                        !_mobState.IsDead(body, mobState) &&
                        _mobState.HasState(body, MobState.Dead, mobState))
                        _mobState.ChangeMobState(body, MobState.Dead, mobState, uid);

                    continue;
                }

                // WOLFGATE(Wolfmed): P3-D23, DestroyOrgan detaches and wounds; RecursiveDeleteEntity reaches here while a
                // mob terminates, which is the DebugAssertException WP9 fixed in WolfmedBodyPartLifecycleSystem.
                if (TerminatingOrDeleted(uid))
                    continue;

                // WOLFGATE(Wolfmed): revival pass, a failed organ stays in at 0 health for the heal surgery.
                if (_wgFailure.Keep((uid, organ, slotted)))
                    continue;

                DestroyOrgan((uid, organ, slotted));
            }
        }

        public void SetHealth(Entity<WolfmedOrganComponent> organ, FixedPoint2 health)
        {
            var wasFunctional = organ.Comp.Health > FixedPoint2.Zero;
            organ.Comp.Health = FixedPoint2.Clamp(health, FixedPoint2.Zero, organ.Comp.MaxHealth);
            Dirty(organ);

            var functional = organ.Comp.Health > FixedPoint2.Zero;
            // WOLFGATE(Performance) START: an organ that just failed is handled next tick, not at the next timed scan
            if (wasFunctional && !functional)
                _wgNextScan = TimeSpan.Zero;
            // WOLFGATE END
            // WOLFGATE(Wolfmed): the owning body is on Wolfgate's OrganComponent, not on the health component.
            if (wasFunctional == functional || CompOrNull<OrganComponent>(organ)?.Body is not { } body)
                return;

            var changed = new OrganFunctionChangedEvent(body, functional);
            RaiseLocalEvent(organ, ref changed);
        }

        public void ChangeHealth(Entity<WolfmedOrganComponent> organ, FixedPoint2 amount) =>
            SetHealth(organ, organ.Comp.Health + amount);

        private void DestroyOrgan(Entity<WolfmedOrganComponent, OrganComponent> organ)
        {
            var parent = Transform(organ).ParentUid;
            // WOLFGATE(Wolfmed): Wolfgate's SharedBodySystem has no TryGetOrganInSlot/TryRemoveOrgan; RemoveOrgan locates the
            // containing part itself, so Onyx's per-slot walk is unnecessary.
            if (HasComp<BodyPartComponent>(parent) && _body.RemoveOrgan(organ.Owner, organ.Comp2))
            {
                // WOLFGATE(Wolfmed): P3-D23, never wound a part that is already terminating (see the Update guard).
                if (organ.Comp1.DestructionWound is { } wound &&
                    organ.Comp1.DestructionWoundSeverity > FixedPoint2.Zero &&
                    !TerminatingOrDeleted(parent) &&
                    HasComp<WoundableComponent>(parent))
                    _wounds.CreateOrMergeWound(parent, wound, organ.Comp1.DestructionWoundSeverity);
            }

            QueueDel(organ.Owner);
        }
    }
}
