using Content.Shared._WF.Fauna;
using Content.Shared._WF.RMCCore.Damage;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffect;
using Content.Server.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Switches <see cref="FaunaEnrageComponent"/> mobs in and out of their enraged stats as their damage crosses the threshold.
/// </summary>
public sealed partial class FaunaEnrageSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedRMCDamageableSystem _rmcDamageable = default!;
    [Dependency] private StatusEffectsSystem _status = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaEnrageComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<FaunaEnrageComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FaunaEnrageComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<FaunaEnrageComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
        SubscribeLocalEvent<FaunaEnrageComponent, GetMeleeAttackRateEvent>(OnGetMeleeAttackRate);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaEnrageComponent>();
        while (query.MoveNext(out var uid, out var enrage))
        {
            if (enrage.Duration == null)
                continue;

            if (enrage.Enraged && now >= enrage.RageEnd)
            {
                SetEnraged((uid, enrage), false);
                enrage.ExhaustionEnd = now + enrage.Exhaustion;
                SetExhausted((uid, enrage), enrage.Exhaustion > TimeSpan.Zero);
            }
            else if (enrage.Exhausted && now >= enrage.ExhaustionEnd)
            {
                SetExhausted((uid, enrage), false);
            }
        }
    }

    private void OnDamageChanged(Entity<FaunaEnrageComponent> ent, ref DamageChangedEvent args)
    {
        var past = _mobState.IsAlive(ent) && args.Damageable.TotalDamage >= ent.Comp.Threshold;
        if (ent.Comp.Duration is not { } duration)
        {
            SetEnraged(ent, past);
            return;
        }

        if (!past || ent.Comp.Enraged || ent.Comp.Exhausted)
            return;

        ent.Comp.RageEnd = _timing.CurTime + duration;
        SetEnraged(ent, true);
    }

    /// <summary>
    /// Starts a timed rage now, unless the mob is already raging or worn out.
    /// </summary>
    public bool TryEnrage(Entity<FaunaEnrageComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false) ||
            ent.Comp.Duration is not { } duration ||
            ent.Comp.Enraged ||
            ent.Comp.Exhausted ||
            !_mobState.IsAlive(ent))
            return false;

        ent.Comp.RageEnd = _timing.CurTime + duration;
        SetEnraged((ent, ent.Comp), true);
        return true;
    }

    private void OnMobStateChanged(Entity<FaunaEnrageComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        SetEnraged(ent, false);
        SetExhausted(ent, false);
    }

    private void OnRefreshSpeed(Entity<FaunaEnrageComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        float speed;
        if (ent.Comp.Enraged)
            speed = ent.Comp.Speed;
        else if (ent.Comp.Exhausted)
            speed = ent.Comp.ExhaustedSpeed;
        else
            return;

        if (!TryComp<MovementSpeedModifierComponent>(ent, out var move)
            || move.BaseWalkSpeed <= 0f
            || move.BaseSprintSpeed <= 0f)
            return;

        args.ModifySpeed(speed / move.BaseWalkSpeed, speed / move.BaseSprintSpeed);
    }

    private void OnGetMeleeDamage(Entity<FaunaEnrageComponent> ent, ref GetMeleeDamageEvent args)
    {
        // Adds the difference to the base attack so bonuses from other handlers still stack.
        if (ent.Comp.Enraged && args.Weapon == ent.Owner && TryComp<MeleeWeaponComponent>(ent, out var melee))
            args.Damage += (ent.Comp.Damage - melee.Damage) * _damageable.UniversalMeleeDamageModifier;
    }

    private void OnGetMeleeAttackRate(Entity<FaunaEnrageComponent> ent, ref GetMeleeAttackRateEvent args)
    {
        if (ent.Comp.Enraged)
            args.Multipliers *= ent.Comp.AttackRate;
    }

    private void SetExhausted(Entity<FaunaEnrageComponent> ent, bool exhausted)
    {
        if (ent.Comp.Exhausted == exhausted)
            return;

        ent.Comp.Exhausted = exhausted;
        if (exhausted && ent.Comp.ExhaustedPopup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner)), ent);

        _movement.RefreshMovementSpeedModifiers(ent);
    }

    private void SetEnraged(Entity<FaunaEnrageComponent> ent, bool enraged)
    {
        if (ent.Comp.Enraged == enraged)
            return;

        ent.Comp.Enraged = enraged;
        _appearance.SetData(ent, FaunaEnrageVisuals.Enraged, enraged);
        if (enraged && ent.Comp.Popup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner)), ent);

        // A heal would end an untimed rage at once, since that rage follows the damage total.
        if (enraged && ent.Comp.Heal > 0 && ent.Comp.Duration != null)
        {
            var heal = _rmcDamageable.DistributeHealing(ent.Owner, "Brute", ent.Comp.Heal);
            _damageable.TryChangeDamage(ent, heal, true, false);
        }

        if (enraged && ent.Comp.ClearStuns)
        {
            _status.TryRemoveStatusEffect(ent, "Stun");
            _status.TryRemoveStatusEffect(ent, "KnockedDown");
        }

        _movement.RefreshMovementSpeedModifiers(ent);

        if (TryComp<FaunaThrowChanceComponent>(ent, out var chance))
        {
            if (enraged)
                ent.Comp.NormalThrowChance = chance.Chance;

            chance.Chance = enraged ? ent.Comp.ThrowChance : ent.Comp.NormalThrowChance;
        }
    }
}
