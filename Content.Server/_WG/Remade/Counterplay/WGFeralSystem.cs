using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Elites;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Remade.Counterplay;

/// <summary>When a Monolith falls its Remade aren't dropped dead: they go feral, hurt and weak, and waste away later.</summary>
public sealed class WGFeralSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly TimeSpan WitherStep = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGFeralComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
        SubscribeLocalEvent<WGFeralComponent, GetMeleeDamageEvent>(OnMelee);
    }

    private void OnSpeed(Entity<WGFeralComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.SpeedFactor);
    }

    private void OnMelee(Entity<WGFeralComponent> ent, ref GetMeleeDamageEvent args)
    {
        args.Damage = args.Damage * ent.Comp.MeleeFactor;
    }

    /// <summary>Cuts every living Remade of a Monolith loose from it. The Undying keeps to its own rules. Returns how many.</summary>
    public int Release(EntityUid monolith)
    {
        var freed = new List<Entity<WGRemadeComponent>>();
        var query = EntityQueryEnumerator<WGRemadeComponent>();
        while (query.MoveNext(out var uid, out var remade))
        {
            if (remade.Source == monolith && !TerminatingOrDeleted(uid) && !_mobState.IsDead(uid) && !HasComp<WGUndyingComponent>(uid))
                freed.Add((uid, remade));
        }

        foreach (var (uid, remade) in freed)
        {
            remade.Source = null;
            var feral = EnsureComp<WGFeralComponent>(uid);
            feral.WithersAt = _timing.CurTime + feral.Lasts;
            _movement.RefreshMovementSpeedModifiers(uid);

            if (_thresholds.TryGetDeadThreshold(uid, out var dead))
            {
                var hurt = new DamageSpecifier(_proto.Index(Blunt), FixedPoint2.New(dead.Value.Float() * feral.HurtFraction));
                _damageable.TryChangeDamage(uid, hurt, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
            }
        }

        return freed.Count;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGFeralComponent>();
        while (query.MoveNext(out var uid, out var feral))
        {
            if (now < feral.WithersAt || now < feral.NextWither || _mobState.IsDead(uid))
                continue;

            feral.NextWither = now + WitherStep;
            var wither = new DamageSpecifier(_proto.Index(Blunt), FixedPoint2.New(feral.WitherPerSecond));
            _damageable.TryChangeDamage(uid, wither, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
        }
    }
}
