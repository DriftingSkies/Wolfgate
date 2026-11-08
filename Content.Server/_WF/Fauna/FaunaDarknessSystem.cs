using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Heals and hides <see cref="FaunaDarknessComponent"/> mobs while no light reaches them, and hurts them in light.
/// </summary>
public sealed partial class FaunaDarknessSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;

    private readonly HashSet<Entity<PointLightComponent>> _lights = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaDarknessComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<FaunaDarknessComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<FaunaDarknessComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaDarknessComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var comp, out var damageable))
        {
            if (now < comp.NextCheck)
                continue;

            comp.NextCheck = now + comp.Interval;
            if (!_mobState.IsAlive(uid))
                continue;

            comp.Dark = !IsLit(uid, comp.LightRange);
            if (!comp.Dark)
            {
                if (comp.LitDamage != null)
                    _damageable.TryChangeDamage(uid, comp.LitDamage, true, false, damageable);

                Reveal((uid, comp));
                continue;
            }

            if (comp.Heal != null && damageable.TotalDamage > 0)
                _damageable.TryChangeDamage(uid, comp.Heal, true, false, damageable);

            if (_helper.TryGetTarget(uid, out _))
            {
                Reveal((uid, comp));
                continue;
            }

            if (comp.Hidden || comp.HiddenVisibility is not { } hidden)
                continue;

            if (!_thresholds.TryGetDeadThreshold(uid, out var dead) ||
                1f - (damageable.TotalDamage / dead.Value).Float() < comp.HideHealth)
                continue;

            if (!TryComp<StealthComponent>(uid, out var stealth))
                continue;

            comp.Hidden = true;
            _stealth.SetVisibility(uid, hidden, stealth);
        }
    }

    /// <summary>
    /// Whether an enabled light other than the mob's own reaches it.
    /// </summary>
    public bool IsLit(EntityUid uid, float range)
    {
        _lights.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, range, _lights, LookupFlags.All | LookupFlags.Approximate);
        foreach (var light in _lights)
        {
            if (light.Owner == uid || !light.Comp.Enabled || light.Comp.Radius < 1f || light.Comp.Energy <= 0f)
                continue;

            if (_container.TryGetContainingContainer(light.Owner, out var container) &&
                container.OccludesLight &&
                !_container.IsInSameOrNoContainer(uid, light.Owner))
                continue;

            if (_examine.InRangeUnOccluded(light.Owner, uid, light.Comp.Radius))
                return true;
        }

        return false;
    }

    private void OnMeleeHit(Entity<FaunaDarknessComponent> ent, ref MeleeHitEvent args)
    {
        if (args.User == ent.Owner)
            Reveal(ent);
    }

    private void OnDamageChanged(Entity<FaunaDarknessComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            Reveal(ent);
    }

    private void OnMobStateChanged(Entity<FaunaDarknessComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Reveal(ent);
    }

    private void Reveal(Entity<FaunaDarknessComponent> ent)
    {
        if (!ent.Comp.Hidden)
            return;

        ent.Comp.Hidden = false;
        if (TryComp<StealthComponent>(ent, out var stealth))
            _stealth.SetVisibility(ent, 1f, stealth);
    }
}
