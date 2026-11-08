using Content.Shared.ActionBlocker;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Events;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.MeridianTraits.Hemophage;

/// <summary>
/// Holds a hemophage's victim helpless for the bite. Every way to break a pull runs through moving or interacting, so
/// blocking those holds the grab; attacks, shots and throws are refused too.
/// </summary>
public sealed partial class WFBeingDrainedSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFBeingDrainedComponent, ComponentStartup>((uid, _, _) => _blocker.UpdateCanMove(uid));
        SubscribeLocalEvent<WFBeingDrainedComponent, ComponentShutdown>((uid, _, _) => _blocker.UpdateCanMove(uid));
        SubscribeLocalEvent<WFBeingDrainedComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
        SubscribeLocalEvent<WFBeingDrainedComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<WFBeingDrainedComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<WFBeingDrainedComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<WFBeingDrainedComponent, ThrowAttemptEvent>(OnThrowAttempt);
    }

    /// <summary>Holds the victim for one bite, lifting by itself after <paramref name="duration"/>.</summary>
    public void Hold(EntityUid victim, EntityUid drinker, TimeSpan duration)
    {
        var comp = EnsureComp<WFBeingDrainedComponent>(victim);
        comp.Drinker = drinker;
        comp.Until = _timing.CurTime + duration;
        Dirty(victim, comp);
    }

    /// <summary>Lets the victim go, unless another drinker has taken hold since.</summary>
    public void Release(EntityUid victim, EntityUid drinker)
    {
        if (TryComp<WFBeingDrainedComponent>(victim, out var comp) && comp.Drinker == drinker)
            RemComp(victim, comp);
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var query = EntityQueryEnumerator<WFBeingDrainedComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime >= comp.Until)
                RemCompDeferred(uid, comp);
        }
    }

    private static bool Holding(WFBeingDrainedComponent comp)
    {
        return comp.LifeStage <= ComponentLifeStage.Running;
    }

    private void OnUpdateCanMove(EntityUid uid, WFBeingDrainedComponent comp, UpdateCanMoveEvent args)
    {
        if (Holding(comp))
            args.Cancel();
    }

    private void OnInteractionAttempt(Entity<WFBeingDrainedComponent> ent, ref InteractionAttemptEvent args)
    {
        if (Holding(ent.Comp))
            args.Cancelled = true;
    }

    private void OnAttackAttempt(EntityUid uid, WFBeingDrainedComponent comp, AttackAttemptEvent args)
    {
        if (Holding(comp))
            args.Cancel();
    }

    private void OnShotAttempted(Entity<WFBeingDrainedComponent> ent, ref ShotAttemptedEvent args)
    {
        if (Holding(ent.Comp))
            args.Cancel();
    }

    private void OnThrowAttempt(EntityUid uid, WFBeingDrainedComponent comp, ThrowAttemptEvent args)
    {
        if (Holding(comp))
            args.Cancel();
    }
}
