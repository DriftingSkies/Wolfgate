using Content.Shared._Mono.Traits.Physical;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;
using System.Linq; // WOLFGATE(Wolfmed)
using Content.Server._WF.Wolfmed.Medical; // WOLFGATE(Wolfmed)
using Content.Shared._Onyx.Wounds; // WOLFGATE(Wolfmed)
using Content.Shared.FixedPoint; // WOLFGATE(Wolfmed)

namespace Content.Server._Mono.Traits.Physical;

/// <summary>
/// Applies slow, uncapped regeneration over all existing damage types for entities with PlateletFactoriesComponent.
/// </summary>
public sealed partial class PlateletFactoriesSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WolfmedReagentWoundMendSystem _woundMend = default!; // WOLFGATE(Wolfmed)

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlateletFactoriesComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<PlateletFactoriesComponent> ent, ref ComponentInit args)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(0.1f, ent.Comp.IntervalSeconds));
        ent.Comp.NextUpdate = _timing.CurTime + interval;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<PlateletFactoriesComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextUpdate > curTime)
                continue;

            var interval = TimeSpan.FromSeconds(Math.Max(0.1f, comp.IntervalSeconds));
            comp.NextUpdate += interval;

            Tick(uid, comp);
        }
    }

    private void Tick(EntityUid uid, PlateletFactoriesComponent comp)
    {
        if (!TryComp<DamageableComponent>(uid, out var damage)
            // || damage.TotalDamage <= 0 // WOLFGATE(Wolfmed): open wounds outlast the damage; checked below
            || _mobState.IsDead(uid))
            return;

        var multiplier = _mobState.IsCritical(uid) ? comp.CritMultiplier : 1f;
        // WOLFGATE(Wolfmed) START: removed damage barely closes a wound, so the trait mends wounds like a healing chem.
        if (damage.TotalDamage > 0)
            _damageable.TryChangeDamage(uid, comp.DamagePerInterval*multiplier, true, false, damage);
        if (HasComp<WoundHostComponent>(uid))
        {
            var healed = comp.DamagePerInterval.DamageDict.Where(pair => pair.Value < 0).ToList();
            _woundMend.Mend(uid, healed.Select(pair => pair.Key).ToList(),
                -healed.Aggregate(FixedPoint2.Zero, (sum, pair) => sum + pair.Value) * multiplier,
                new HashSet<TreatmentCapability> { TreatmentCapability.Biological });
        }
        // WOLFGATE END
    }
}


