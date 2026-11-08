using Content.Server.Explosion.EntitySystems;
using Content.Server.Popups;
using Content.Shared._WF.Fauna;
using Content.Shared.Damage;
using Content.Shared.Jittering;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lights and sets off the fuse of <see cref="FaunaFuseComponent"/> mobs.
/// </summary>
public sealed partial class FaunaFuseSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;

    private const int JitterSteps = 6;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaFuseComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<FaunaFuseComponent, FaunaChargeHitEvent>(OnChargeHit);
        SubscribeLocalEvent<FaunaFuseComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<FaunaFuseComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMeleeHit(Entity<FaunaFuseComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.User != ent.Owner || args.HitEntities.Count == 0 || ent.Comp.ExplodeAt != TimeSpan.Zero)
            return;

        ent.Comp.ExplodeAt = _timing.CurTime + ent.Comp.FuseTime;
        _popup.PopupEntity(Loc.GetString("fauna-fuse-lit", ("user", ent.Owner)), ent, PopupType.LargeCaution);
    }

    private void OnChargeHit(Entity<FaunaFuseComponent> ent, ref FaunaChargeHitEvent args)
    {
        ent.Comp.ExplodeAt = _timing.CurTime;
    }

    private void OnDamageChanged(Entity<FaunaFuseComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null || args.DamageDelta.GetTotal() < ent.Comp.MinDamage)
            return;

        if (_random.Prob(ent.Comp.BurstChance))
            ent.Comp.ExplodeAt = _timing.CurTime;
    }

    private void OnMobStateChanged(Entity<FaunaFuseComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            ent.Comp.ExplodeAt = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaFuseComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Exploded || comp.ExplodeAt == TimeSpan.Zero)
                continue;

            if (comp.ExplodeAt > now)
            {
                if (comp.Jitter > 0)
                    UpdateJitter((uid, comp), now);

                continue;
            }

            comp.Exploded = true;
            _explosion.TriggerExplosive(uid);
        }
    }

    /// <summary>
    /// Raises the jitter in steps as the fuse burns down.
    /// </summary>
    private void UpdateJitter(Entity<FaunaFuseComponent> ent, TimeSpan now)
    {
        var left = (ent.Comp.ExplodeAt - now) / ent.Comp.FuseTime;
        var step = Math.Clamp(JitterSteps - (int) (left * JitterSteps), 1, JitterSteps);
        if (step == ent.Comp.JitterStep)
            return;

        ent.Comp.JitterStep = step;
        _jitter.AddJitter(ent, ent.Comp.Jitter * step / JitterSteps, 8f + step * 2f);
    }
}
