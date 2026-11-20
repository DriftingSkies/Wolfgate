using Content.Shared._WG.Augments;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Emp;
using Content.Shared.Popups;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WG.Augments;

/// <summary>
/// Turns optical camouflage on and off: on, the body takes the implant's stealth until it runs out, attacks, shoots,
/// gets hurt by someone or catches an EMP. Then the action cools down.
/// </summary>
public sealed class WGOpticalCamoSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGOpticalCamoComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WGOpticalCamoComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WGOpticalCamoComponent, WGToggleOpticalCamoEvent>(OnToggle);
        SubscribeLocalEvent<WGOpticalCamoComponent, MeleeAttackEvent>(OnMeleeAttack);
        SubscribeLocalEvent<WGOpticalCamoComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<WGOpticalCamoComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<WGOpticalCamoComponent, EmpPulseEvent>(OnEmp);
    }

    private void OnInit(Entity<WGOpticalCamoComponent> ent, ref ComponentInit args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<WGOpticalCamoComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.EndsAt != null && !TerminatingOrDeleted(ent))
            EntityManager.RemoveComponents(ent, ent.Comp.Components);

        ent.Comp.EndsAt = null;
        QueueDel(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnToggle(Entity<WGOpticalCamoComponent> ent, ref WGToggleOpticalCamoEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.EndsAt != null)
        {
            Reveal((ent, ent.Comp));
            return;
        }

        // Stealth from something else is left alone; the camouflage won't stack with it or strip it later.
        if (HasComp<StealthComponent>(ent))
        {
            _popup.PopupEntity(Loc.GetString("wg-optical-camo-blocked"), ent, ent);
            return;
        }

        args.Handled = true;
        EntityManager.AddComponents(ent, ent.Comp.Components, removeExisting: false);
        if (TryComp<StealthComponent>(ent, out var stealth))
            _stealth.SetVisibility(ent, stealth.MinVisibility, stealth);

        ent.Comp.EndsAt = _timing.CurTime + ent.Comp.Duration;
        _actions.SetToggled(ent.Comp.ActionEntity, true);
        _audio.PlayPvs(ent.Comp.OnSound, ent);
    }

    private void OnMeleeAttack(Entity<WGOpticalCamoComponent> ent, ref MeleeAttackEvent args)
    {
        Reveal((ent, ent.Comp));
    }

    private void OnShotAttempted(Entity<WGOpticalCamoComponent> ent, ref ShotAttemptedEvent args)
    {
        // The attempt comes before the gun's own checks: a dry click or a trigger held between shots fires nothing.
        var gun = args.Used.Comp;
        if (args.Cancelled
            || gun.NextFire > _timing.CurTime
            || gun.SelectedMode == SelectiveFire.SemiAuto && gun.ShotCounter > 0)
            return;

        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(args.Used, ref ammo);
        if (ammo.Capacity > 0 && ammo.Count <= 0)
            return;

        Reveal((ent, ent.Comp));
    }

    private void OnDamaged(Entity<WGOpticalCamoComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased && args.Origin is { } origin && origin != ent.Owner)
            Reveal((ent, ent.Comp));
    }

    private void OnEmp(Entity<WGOpticalCamoComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        Reveal((ent, ent.Comp), ent.Comp.EmpCooldown);
    }

    /// <summary>Drops the body's camouflage if it is on, and starts the action's cooldown.</summary>
    public void Reveal(Entity<WGOpticalCamoComponent?> ent, TimeSpan? cooldown = null)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        var wasOn = ent.Comp.EndsAt != null;
        if (wasOn)
        {
            EntityManager.RemoveComponents(ent, ent.Comp.Components);
            ent.Comp.EndsAt = null;
            _actions.SetToggled(ent.Comp.ActionEntity, false);
            _audio.PlayPvs(ent.Comp.OffSound, ent);
            _popup.PopupEntity(Loc.GetString("wg-optical-camo-off"), ent, ent);
        }

        // An EMP knocks it out whether it was on or not.
        if (wasOn || cooldown != null)
            _actions.SetCooldown(ent.Comp.ActionEntity, cooldown ?? ent.Comp.Cooldown);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGOpticalCamoComponent>();
        while (query.MoveNext(out var uid, out var camo))
        {
            if (camo.EndsAt is { } end && now >= end)
                Reveal((uid, camo));
        }
    }
}
