using Content.Server.Explosion.EntitySystems;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Timing;

namespace Content.Server._WF.Vehicles.Weapons;

/// <summary>Sets off vehicle gun shells: on a timer from the gun, or when a round reaches the aimed point.</summary>
public sealed partial class VehicleTriggerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        // A round that hit something is queued for deletion as its flight ends; it goes off in the same tick, while it is still there.
        UpdatesAfter.Add(typeof(ShootAtFixedPointSystem));

        SubscribeLocalEvent<OnShootTriggerAmmoTimerComponent, AmmoShotEvent>(OnShot);
        SubscribeLocalEvent<TriggerOnFixedDistanceStopComponent, ProjectileFixedDistanceStopEvent>(OnFixedDistanceStop);
    }

    private void OnShot(Entity<OnShootTriggerAmmoTimerComponent> ent, ref AmmoShotEvent args)
    {
        foreach (var projectile in args.FiredProjectiles)
        {
            _trigger.HandleTimerTrigger(projectile, null, ent.Comp.Delay, ent.Comp.BeepInterval, ent.Comp.InitialBeepDelay, ent.Comp.BeepSound);
        }
    }

    private void OnFixedDistanceStop(Entity<TriggerOnFixedDistanceStopComponent> ent, ref ProjectileFixedDistanceStopEvent args)
    {
        EnsureComp<ActiveTriggerOnFixedDistanceStopComponent>(ent).TriggerAt = _timing.CurTime + ent.Comp.Delay;
    }

    public override void Update(float frameTime)
    {
        var time = _timing.CurTime;
        var query = EntityQueryEnumerator<ActiveTriggerOnFixedDistanceStopComponent>();
        while (query.MoveNext(out var uid, out var active))
        {
            if (time < active.TriggerAt)
                continue;

            _trigger.Trigger(uid);

            if (!TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid))
                QueueDel(uid);
        }
    }
}
