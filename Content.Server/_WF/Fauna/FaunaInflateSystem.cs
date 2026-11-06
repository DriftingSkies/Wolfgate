using Content.Server.NPC.HTN;
using Content.Shared._WF.Fauna;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Inflates and deflates <see cref="FaunaInflateComponent"/> mobs.
/// </summary>
public sealed partial class FaunaInflateSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private const string NavSmash = "NavSmash";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaInflateComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FaunaInflateComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<FaunaInflateComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    private void OnMobStateChanged(Entity<FaunaInflateComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && ent.Comp.Inflated)
            Deflate(ent);
    }

    private void OnRefreshSpeed(Entity<FaunaInflateComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!ent.Comp.Inflated
            || !TryComp<MovementSpeedModifierComponent>(ent, out var move)
            || move.BaseWalkSpeed <= 0f
            || move.BaseSprintSpeed <= 0f)
            return;

        args.ModifySpeed(ent.Comp.Speed / move.BaseWalkSpeed, ent.Comp.Speed / move.BaseSprintSpeed);
    }

    private void OnGetMeleeDamage(Entity<FaunaInflateComponent> ent, ref GetMeleeDamageEvent args)
    {
        // Adds the difference to the base attack so bonuses from other handlers still stack.
        if (ent.Comp.Inflated && args.Weapon == ent.Owner && TryComp<MeleeWeaponComponent>(ent, out var melee))
            args.Damage += (ent.Comp.Damage - melee.Damage) * _damageable.UniversalMeleeDamageModifier;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaInflateComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var comp, out var htn))
        {
            if (now < comp.NextChange || !_mobState.IsAlive(uid))
                continue;

            if (comp.Inflated)
            {
                Deflate((uid, comp));
                comp.NextChange = now + comp.Cooldown;
                continue;
            }

            if (!htn.Blackboard.TryGetValue<EntityUid>(FaunaHelperSystem.TargetKey, out _, EntityManager))
                continue;

            Inflate((uid, comp), htn);
            comp.NextChange = now + comp.Duration;
        }
    }

    private void Inflate(Entity<FaunaInflateComponent> ent, HTNComponent htn)
    {
        ent.Comp.Inflated = true;
        if (TryComp<DamageableComponent>(ent, out var damageable))
        {
            ent.Comp.NormalModifierSet = damageable.DamageModifierSetId;
            _damageable.SetDamageModifierSetId(ent, ent.Comp.ModifierSet, damageable);
        }

        _movement.RefreshMovementSpeedModifiers(ent);
        htn.Blackboard.SetValue(NavSmash, true);
        _appearance.SetData(ent, FaunaInflateVisuals.Inflated, true);
        if (ent.Comp.Popup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner)), ent, PopupType.MediumCaution);
    }

    private void Deflate(Entity<FaunaInflateComponent> ent)
    {
        ent.Comp.Inflated = false;
        if (TryComp<DamageableComponent>(ent, out var damageable))
            _damageable.SetDamageModifierSetId(ent, ent.Comp.NormalModifierSet, damageable);

        _movement.RefreshMovementSpeedModifiers(ent);
        if (TryComp<HTNComponent>(ent, out var htn))
            htn.Blackboard.SetValue(NavSmash, false);

        _appearance.SetData(ent, FaunaInflateVisuals.Inflated, false);
    }
}
