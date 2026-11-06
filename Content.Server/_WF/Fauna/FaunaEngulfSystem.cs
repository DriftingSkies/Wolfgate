using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Engulfs downed humanoids that <see cref="FaunaEngulfComponent"/> mobs hit, damages captives and drops dead ones.
/// </summary>
public sealed partial class FaunaEngulfSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private readonly List<EntityUid> _captives = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaEngulfComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FaunaEngulfComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<FaunaEngulfComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FaunaEngulfComponent, EntityTerminatingEvent>(OnTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaEngulfComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Container == null || comp.Container.ContainedEntities.Count == 0 || now < comp.NextTick)
                continue;

            comp.NextTick = now + comp.Interval;
            if (!_mobState.IsAlive(uid))
                continue;

            _captives.Clear();
            _captives.AddRange(comp.Container.ContainedEntities);
            foreach (var captive in _captives)
            {
                if (comp.CaptiveDamage != null && _random.Prob(comp.CaptiveChance))
                    _damageable.TryChangeDamage(captive, comp.CaptiveDamage, origin: uid);

                if (_mobState.IsDead(captive))
                    _container.Remove(captive, comp.Container);
            }
        }
    }

    private void OnMapInit(Entity<FaunaEngulfComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Container = _container.EnsureContainer<Container>(ent, ent.Comp.ContainerId);
        ent.Comp.NextTick = _timing.CurTime + ent.Comp.Interval;
    }

    private void OnMeleeHit(Entity<FaunaEngulfComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.User != ent.Owner || args.HitEntities.Count == 0)
            return;

        var target = args.HitEntities[0];
        if (!HasComp<HumanoidAppearanceComponent>(target) || _mobState.IsDead(target) ||
            (!HasComp<KnockedDownComponent>(target) && !_mobState.IsCritical(target)) ||
            _container.IsEntityInContainer(target))
            return;

        if (!_container.Insert(target, ent.Comp.Container))
            return;

        if (_thresholds.TryGetDeadThreshold(ent, out var dead))
            _thresholds.SetMobStateThreshold(ent, dead.Value + ent.Comp.ThresholdBonus, MobState.Dead);

        if (ent.Comp.Heal != null)
            _damageable.TryChangeDamage(ent, ent.Comp.Heal, true, false);

        _audio.PlayPvs(ent.Comp.Sound, ent);
        if (ent.Comp.Popup is { } popup)
        {
            var message = Loc.GetString(popup, ("user", ent.Owner), ("target", target));
            _popup.PopupEntity(message, ent, PopupType.LargeCaution);
        }
    }

    private void OnMobStateChanged(Entity<FaunaEngulfComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            _container.EmptyContainer(ent.Comp.Container);
    }

    private void OnTerminating(Entity<FaunaEngulfComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Container != null)
            _container.EmptyContainer(ent.Comp.Container);
    }
}
