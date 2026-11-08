using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Hits back at whoever grabs or shoves a <see cref="FaunaGrabRetortComponent"/> mob. Pulls are broken on the next
/// tick, since stopping one inside its start event leaves the puller half attached.
/// </summary>
public sealed partial class FaunaGrabRetortSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    private readonly List<(EntityUid Mob, EntityUid User)> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaGrabRetortComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<FaunaGrabRetortComponent, DisarmedEvent>(OnDisarmed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0)
            return;

        var pending = _pending.ToArray();
        _pending.Clear();
        foreach (var (mob, user) in pending)
        {
            if (TerminatingOrDeleted(mob) || TerminatingOrDeleted(user)
                || !TryComp<FaunaGrabRetortComponent>(mob, out var comp)
                || !_mobState.IsAlive(mob)
                || _container.IsEntityInContainer(user)
                || Transform(mob).MapID != Transform(user).MapID)
                continue;

            if (TryComp<PullableComponent>(mob, out var pullable) && pullable.Puller == user)
                _pulling.TryStopPull(mob, pullable);

            Retort((mob, comp), user);
        }
    }

    private void OnPullStarted(EntityUid uid, FaunaGrabRetortComponent comp, PullStartedMessage args)
    {
        if (args.PulledUid == uid && _mobState.IsAlive(uid))
            _pending.Add((uid, args.PullerUid));
    }

    private void OnDisarmed(Entity<FaunaGrabRetortComponent> ent, ref DisarmedEvent args)
    {
        if (_mobState.IsAlive(ent))
            _pending.Add((ent, args.Source));
    }

    private void Retort(Entity<FaunaGrabRetortComponent> ent, EntityUid user)
    {
        if (ent.Comp.Damage is { } damage)
            _damageable.TryChangeDamage(user, damage, origin: ent);
        else if (TryComp<MeleeWeaponComponent>(ent, out var melee))
            _damageable.TryChangeDamage(user, melee.Damage, origin: ent);

        if (ent.Comp.Knockdown is { } knockdown)
            _stun.TryKnockdown(user, knockdown, true);

        var away = _transform.GetWorldPosition(user) - _transform.GetWorldPosition(ent);
        if (away.LengthSquared() < 0.01f)
            away = _random.NextAngle().ToVec();

        _throwing.TryThrow(user, away.Normalized() * _random.NextFloat(ent.Comp.MinThrow, ent.Comp.MaxThrow), 10f, ent,
            pushbackRatio: 0f);
        _audio.PlayPvs(ent.Comp.Sound, ent);
        if (ent.Comp.Popup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner), ("target", user)), ent, PopupType.MediumCaution);
    }
}
