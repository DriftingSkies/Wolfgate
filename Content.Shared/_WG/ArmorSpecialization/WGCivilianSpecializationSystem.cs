using Content.Shared._WG.Wolfmed.Wounds;
using Content.Shared.Actions;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WG.ArmorSpecialization;

/// <summary>
/// The civilian suit passives that answer events at the moment they happen: steady hands, sterile weave, servo
/// assist, rad sink, wildlife padding, haul frame, blast liner, the mime's vow, suit thrusters, EMP hardening and
/// decon coating.
/// </summary>
public sealed partial class WGCivilianSpecializationSystem : EntitySystem
{
    private const string Radiation = "Radiation";

    [Dependency] private HeldSpeedModifierSystem _heldSpeed = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSteadyHandsComponent, InventoryRelayedEvent<WolfmedSteadyHandsEvent>>(OnSteadyHands);
        SubscribeLocalEvent<WGSteadyHandsComponent, ExaminedEvent>(OnSteadyHandsExamined);

        SubscribeLocalEvent<WGSterileWeaveComponent, InventoryRelayedEvent<WolfmedSterileEvent>>(OnSterile);
        SubscribeLocalEvent<WGSterileWeaveComponent, ExaminedEvent>(OnSterileExamined);

        SubscribeLocalEvent<WGServoAssistComponent, InventoryRelayedEvent<WGToolSpeedEvent>>(OnToolSpeed);
        SubscribeLocalEvent<WGServoAssistComponent, ExaminedEvent>(OnServoExamined);

        SubscribeLocalEvent<WGRadSinkComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnRadSinkDamage);
        SubscribeLocalEvent<WGRadSinkComponent, ExaminedEvent>(OnRadSinkExamined);

        SubscribeLocalEvent<WGWildlifePaddingComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnWildlifeDamage);
        SubscribeLocalEvent<WGWildlifePaddingComponent, ExaminedEvent>(OnWildlifeExamined);

        SubscribeLocalEvent<WGHaulFrameComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnHaulSpeed);
        SubscribeLocalEvent<WGHaulFrameComponent, GotEquippedEvent>(OnHaulEquipped);
        SubscribeLocalEvent<WGHaulFrameComponent, GotUnequippedEvent>(OnHaulUnequipped);
        SubscribeLocalEvent<WGHaulFrameComponent, ExaminedEvent>(OnHaulExamined);

        SubscribeLocalEvent<WGBlastLinerComponent, InventoryRelayedEvent<WolfmedSeverAttemptEvent>>(OnBlastSever);
        SubscribeLocalEvent<WGBlastLinerComponent, InventoryRelayedEvent<WolfmedWoundRuleAttemptEvent>>(OnBlastWound);
        SubscribeLocalEvent<WGBlastLinerComponent, InventoryRelayedEvent<WolfmedOrganBlastAttemptEvent>>(OnBlastOrgans);
        SubscribeLocalEvent<WGBlastLinerComponent, ExaminedEvent>(OnBlastExamined);

        SubscribeLocalEvent<WGVowOfSilenceComponent, InventoryRelayedEvent<WolfmedPainNoiseAttemptEvent>>(OnPainNoise);
        SubscribeLocalEvent<WGVowOfSilenceComponent, ExaminedEvent>(OnVowExamined);

        SubscribeLocalEvent<WGSuitThrustersComponent, GetItemActionsEvent>(OnThrusterActions);
        SubscribeLocalEvent<WGSuitThrustersComponent, WGSuitThrustersEvent>(OnThrust);
        SubscribeLocalEvent<WGSuitThrustersComponent, ExaminedEvent>(OnThrustersExamined);

        SubscribeLocalEvent<WGEmpHardenedComponent, ExaminedEvent>(OnEmpHardenedExamined);
        SubscribeLocalEvent<WGDeconCoatingComponent, ExaminedEvent>(OnDeconExamined);
    }

    private void OnSteadyHands(Entity<WGSteadyHandsComponent> ent, ref InventoryRelayedEvent<WolfmedSteadyHandsEvent> args)
    {
        args.Args.Multiplier = MathF.Max(args.Args.Multiplier, ent.Comp.Multiplier);
    }

    private void OnSterile(Entity<WGSterileWeaveComponent> ent, ref InventoryRelayedEvent<WolfmedSterileEvent> args)
    {
        if (args.Args.Part is not { } part || WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, part))
            args.Args.Sterile = true;
    }

    private void OnToolSpeed(Entity<WGServoAssistComponent> ent, ref InventoryRelayedEvent<WGToolSpeedEvent> args)
    {
        args.Args.Multiplier = MathF.Max(args.Args.Multiplier, ent.Comp.Multiplier);
    }

    private void OnRadSinkDamage(Entity<WGRadSinkComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        if (!args.Args.Damage.DamageDict.TryGetValue(Radiation, out var amount) || amount <= 0)
            return;

        var damage = new DamageSpecifier(args.Args.Damage);
        damage.DamageDict[Radiation] = amount * (1f - Math.Clamp(ent.Comp.Reduction, 0f, 1f));
        args.Args.Damage = damage;
    }

    private void OnWildlifeDamage(Entity<WGWildlifePaddingComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        if (args.Args.Origin is not { } attacker || !IsCreature(attacker) || args.Args.Damage.GetTotal() <= 0)
            return;

        args.Args.Damage = args.Args.Damage * ent.Comp.Multiplier;
    }

    /// <summary>
    /// Whether <paramref name="uid"/> wears an EMP-hardened suit or is carried, at any depth, by someone who does.
    /// The EMP system asks this before it pulses anything.
    /// </summary>
    public bool ShieldsFromEmp(EntityUid uid)
    {
        var current = uid;
        for (var depth = 0; depth < 16; depth++)
        {
            if (_inventory.TryGetSlotEntity(current, "outerClothing", out var suit) && HasComp<WGEmpHardenedComponent>(suit))
                return true;

            if (!_containers.TryGetContainingContainer((current, null, null), out var container))
                return false;

            current = container.Owner;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="reagent"/> touching <paramref name="target"/> is kept off by a decon-coated suit. The
    /// reaction system asks this for every touch, except inside <see cref="BypassDecon"/>. Anything that puts out fires
    /// still gets through, so an extinguisher works on the wearer.
    /// </summary>
    public bool BlocksTouch(EntityUid target, ReagentPrototype reagent) =>
        !_deconBypass && reagent.ReactiveEffects?.ContainsKey(ExtinguishGroup) != true &&
        _inventory.TryGetSlotEntity(target, "outerClothing", out var suit) && HasComp<WGDeconCoatingComponent>(suit);

    private const string ExtinguishGroup = "Extinguish";

    /// <summary>Runs a deliberate wash (a shower, antiseptic) that a decon coating lets through.</summary>
    public void BypassDecon(Action action)
    {
        _deconBypass = true;
        try
        {
            action();
        }
        finally
        {
            _deconBypass = false;
        }
    }

    private bool _deconBypass;

    /// <summary>A living thing that isn't a person or a borg: carp, spiders, goliaths and the like.</summary>
    private bool IsCreature(EntityUid uid) =>
        HasComp<MobStateComponent>(uid) && !HasComp<HumanoidAppearanceComponent>(uid) && !HasComp<BorgChassisComponent>(uid);

    #region Haul frame

    /// <summary>Undoes the pull slowdown the pulling system applies to the wearer.</summary>
    private void OnHaulSpeed(Entity<WGHaulFrameComponent> ent, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        if (!_inventory.TryGetContainingEntity(ent.Owner, out var wearer) ||
            !TryComp(wearer, out PullerComponent? puller) || puller.Pulling is not { } pulled)
            return;

        var (walk, sprint) = TryComp(pulled, out HeldSpeedModifierComponent? held)
            ? _heldSpeed.GetHeldMovementSpeedModifiers(pulled, held)
            : (puller.WalkSpeedModifier, puller.SprintSpeedModifier);

        if (walk > 0f && sprint > 0f)
            args.Args.ModifySpeed(MathF.Max(1f, 1f / walk), MathF.Max(1f, 1f / sprint));
    }

    private void OnHaulEquipped(Entity<WGHaulFrameComponent> ent, ref GotEquippedEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.Equipee);
    }

    private void OnHaulUnequipped(Entity<WGHaulFrameComponent> ent, ref GotUnequippedEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.Equipee);
    }

    #endregion

    private void OnBlastSever(Entity<WGBlastLinerComponent> ent, ref InventoryRelayedEvent<WolfmedSeverAttemptEvent> args)
    {
        if (args.Args.IsExplosion && WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            args.Args.Cancelled = true;
    }

    private void OnBlastWound(Entity<WGBlastLinerComponent> ent, ref InventoryRelayedEvent<WolfmedWoundRuleAttemptEvent> args)
    {
        if ((args.Args.Cause & WolfmedWoundCause.Explosion) != 0 && ent.Comp.Blocks.Contains(args.Args.Wound) &&
            WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            args.Args.Cancelled = true;
    }

    private void OnBlastOrgans(Entity<WGBlastLinerComponent> ent, ref InventoryRelayedEvent<WolfmedOrganBlastAttemptEvent> args)
    {
        if (WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            args.Args.Cancelled = true;
    }

    private void OnPainNoise(Entity<WGVowOfSilenceComponent> ent, ref InventoryRelayedEvent<WolfmedPainNoiseAttemptEvent> args)
    {
        args.Args.Cancelled = true;
    }

    #region Thrusters

    private void OnThrusterActions(Entity<WGSuitThrustersComponent> ent, ref GetItemActionsEvent args)
    {
        if (!TryComp<ClothingComponent>(ent, out var clothing) || args.SlotFlags is not { } slot ||
            (clothing.Slots & slot) == 0)
            return;

        args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    /// <summary>One burst the way the wearer faces. Only in zero gravity; a wasted press doesn't spend the charge.</summary>
    private void OnThrust(Entity<WGSuitThrustersComponent> ent, ref WGSuitThrustersEvent args)
    {
        var wearer = args.Performer;
        if (!_gravity.IsWeightless(wearer))
        {
            _popup.PopupClient(Loc.GetString("wg-armor-spec-thrusters-gravity"), wearer, wearer);
            return;
        }

        if (!TryComp(wearer, out PhysicsComponent? physics))
            return;

        var direction = _xform.GetWorldRotation(wearer).ToWorldVec();
        _physics.SetLinearVelocity(wearer, physics.LinearVelocity + direction * ent.Comp.Speed, body: physics);
        _popup.PopupClient(Loc.GetString("wg-armor-spec-thrusters-fired"), wearer, wearer);
        args.Handled = true;
    }

    #endregion

    #region Examine

    private void OnSteadyHandsExamined(Entity<WGSteadyHandsComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wg-armor-spec-steady-hands-examine"));

    private void OnSterileExamined(Entity<WGSterileWeaveComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wg-armor-spec-sterile-examine"));

    private void OnServoExamined(Entity<WGServoAssistComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wg-armor-spec-servo-examine",
            ("percent", (int) MathF.Round((ent.Comp.Multiplier - 1f) * 100f))));

    private void OnRadSinkExamined(Entity<WGRadSinkComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wg-armor-spec-rad-sink-examine",
            ("percent", (int) MathF.Round(ent.Comp.Reduction * 100f))));

    private void OnWildlifeExamined(Entity<WGWildlifePaddingComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wg-armor-spec-wildlife-examine",
            ("percent", (int) MathF.Round((1f - ent.Comp.Multiplier) * 100f))));

    private void OnHaulExamined(Entity<WGHaulFrameComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wg-armor-spec-haul-frame-examine"));

    private void OnBlastExamined(Entity<WGBlastLinerComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wg-armor-spec-blast-liner-examine"));

    private void OnVowExamined(Entity<WGVowOfSilenceComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wg-armor-spec-vow-examine"));

    private void OnThrustersExamined(Entity<WGSuitThrustersComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wg-armor-spec-thrusters-examine"));

    private void OnEmpHardenedExamined(Entity<WGEmpHardenedComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wg-armor-spec-emp-hardened-examine"));

    private void OnDeconExamined(Entity<WGDeconCoatingComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wg-armor-spec-decon-examine"));

    #endregion
}

/// <summary>The suit thrusters' action.</summary>
public sealed partial class WGSuitThrustersEvent : InstantActionEvent;
