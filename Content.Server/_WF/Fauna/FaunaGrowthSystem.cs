using Content.Shared.Damage;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Grows young fauna into adults once their time comes, keeping their share of damage and any mind.
/// </summary>
public sealed partial class FaunaGrowthSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaGrowthComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<FaunaGrowthComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.GrowAt = _timing.CurTime + _random.Next(ent.Comp.MinDelay, ent.Comp.MaxDelay);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaGrowthComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var growth, out var xform))
        {
            if (now < growth.GrowAt || !_mobState.IsAlive(uid))
                continue;

            if (!_random.Prob(growth.Chance))
            {
                RemCompDeferred<FaunaGrowthComponent>(uid);
                continue;
            }

            Grow((uid, growth, xform));
        }
    }

    /// <summary>
    /// Swaps the young mob for its adult form on the same spot.
    /// </summary>
    public void Grow(Entity<FaunaGrowthComponent, TransformComponent> ent)
    {
        var proto = ent.Comp1.GrowIntoAny.Count > 0 ? _random.Pick(ent.Comp1.GrowIntoAny) : ent.Comp1.GrowInto;
        var adult = Spawn(proto, _transform.GetMapCoordinates(ent.Owner, ent.Comp2));
        _transform.SetLocalRotation(adult, ent.Comp2.LocalRotation);

        if (TryComp<DamageableComponent>(adult, out var damageable) &&
            _mobThreshold.GetScaledDamage(ent.Owner, adult, out var damage) &&
            damage != null)
            _damageable.SetDamage(adult, damageable, damage);

        if (_mind.TryGetMind(ent.Owner, out var mindId, out var mind))
            _mind.TransferTo(mindId, adult, mind: mind);

        QueueDel(ent.Owner);
    }
}
