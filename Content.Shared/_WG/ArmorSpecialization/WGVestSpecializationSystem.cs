using Content.Shared._Goobstation.DoAfter;
using Content.Shared._Mono.MotionDetector.Components;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.ProximityDetection;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WG.ArmorSpecialization;

/// <summary>
/// Vest specializations: fast hands, low signature, unrestricted and second wind. Each reads the wearer's outer
/// clothing slot. Modular plates are Mono's armor plate holder (<c>ClothingArmorPlate</c>).
/// </summary>
public sealed class WGVestSpecializationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private const string Slot = "outerClothing";

    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        // Pairs verified free: nothing else subscribes these events on InventoryComponent.
        SubscribeLocalEvent<InventoryComponent, GetDoAfterDelayMultiplierEvent>(OnDelayMultiplier);
        SubscribeLocalEvent<InventoryComponent, ProximityDetectionAttemptEvent>(OnProximityDetection);
        SubscribeLocalEvent<InventoryComponent, GunRefreshModifiersEvent>(OnGunRefresh);

        SubscribeLocalEvent<WGSecondWindStateComponent, RefreshMovementSpeedModifiersEvent>(OnSecondWindSpeed);
        SubscribeLocalEvent<WGUnrestrictedComponent, GotEquippedEvent>((_, _, args) => RefreshHeldGuns(args.Equipee));
        SubscribeLocalEvent<WGUnrestrictedComponent, GotUnequippedEvent>((_, _, args) => RefreshHeldGuns(args.Equipee));

        SubscribeLocalEvent<WGFastHandsComponent, ExaminedEvent>((_, _, args) => args.PushMarkup(Loc.GetString("wg-armor-spec-fast-hands-examine")));
        SubscribeLocalEvent<WGLowSignatureComponent, ExaminedEvent>((_, _, args) => args.PushMarkup(Loc.GetString("wg-armor-spec-low-signature-examine")));
        SubscribeLocalEvent<WGUnrestrictedComponent, ExaminedEvent>((_, _, args) => args.PushMarkup(Loc.GetString("wg-armor-spec-unrestricted-examine")));
        SubscribeLocalEvent<WGSecondWindComponent, ExaminedEvent>((_, _, args) => args.PushMarkup(Loc.GetString("wg-armor-spec-second-wind-examine")));
    }

    private bool TryGetVest<T>(EntityUid wearer, out T comp) where T : IComponent
    {
        comp = default!;
        return _inventory.TryGetSlotEntity(wearer, Slot, out var vest) && TryComp(vest, out comp!);
    }

    private void OnDelayMultiplier(EntityUid uid, InventoryComponent inv, GetDoAfterDelayMultiplierEvent args)
    {
        if (TryGetVest<WGFastHandsComponent>(uid, out var hands))
            args.Multiplier *= hands.Multiplier;
    }

    private void OnProximityDetection(Entity<InventoryComponent> ent, ref ProximityDetectionAttemptEvent args)
    {
        if (HasComp<MotionDetectorIgnoreHolderComponent>(args.Detector.Owner) && TryGetVest<WGLowSignatureComponent>(ent, out _))
            args.Cancel = true;
    }

    private void OnGunRefresh(Entity<InventoryComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!TryGetVest<WGUnrestrictedComponent>(ent, out var free))
            return;

        args.MinAngle *= free.Spread;
        args.MaxAngle *= free.Spread;
        args.AngleIncrease *= free.Spread;
    }

    /// <summary>Recomputes the spread of every gun the wearer holds, as an unrestricted vest goes on or comes off.</summary>
    private void RefreshHeldGuns(EntityUid wearer)
    {
        foreach (var held in _hands.EnumerateHeld(wearer))
        {
            if (TryComp<GunComponent>(held, out var gun))
                _gun.RefreshModifiers((held, gun), wearer);
        }
    }

    private void OnSecondWindSpeed(Entity<WGSecondWindStateComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (_timing.CurTime < ent.Comp.ActiveUntil)
            args.ModifySpeed(ent.Comp.SpeedMultiplier, ent.Comp.SpeedMultiplier);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer || _timing.CurTime < _nextCheck)
            return;
        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(0.25);

        // Recovery and burst expiry run on the wearer, vest or not.
        var states = EntityQueryEnumerator<WGSecondWindStateComponent, DamageableComponent>();
        while (states.MoveNext(out var wearer, out var state, out var damage))
        {
            if (state.ActiveUntil != TimeSpan.Zero && _timing.CurTime >= state.ActiveUntil)
            {
                state.ActiveUntil = TimeSpan.Zero;
                Dirty(wearer, state);
                _movementSpeed.RefreshMovementSpeedModifiers(wearer);
            }

            if (state.Spent && damage.TotalDamage < state.Threshold / 2)
            {
                state.Spent = false;
                Dirty(wearer, state);
            }
        }

        var query = EntityQueryEnumerator<WGSecondWindComponent>();
        while (query.MoveNext(out var uid, out var wind))
        {
            // Only a worn vest enables it; one carried in hand does nothing.
            if (!_inventory.TryGetContainingEntity(uid, out var wearer)
                || !_inventory.TryGetSlotEntity(wearer.Value, Slot, out var worn)
                || worn != uid
                || !TryComp<DamageableComponent>(wearer, out var damage))
                continue;

            var state = EnsureComp<WGSecondWindStateComponent>(wearer.Value);
            if (state.Spent || damage.TotalDamage < wind.Threshold)
                continue;

            state.Spent = true;
            state.Threshold = wind.Threshold;
            state.SpeedMultiplier = wind.SpeedMultiplier;
            state.ActiveUntil = _timing.CurTime + wind.Duration;
            Dirty(wearer.Value, state);
            _movementSpeed.RefreshMovementSpeedModifiers(wearer.Value);
            _popup.PopupEntity(Loc.GetString("wg-armor-spec-second-wind-popup"), wearer.Value, wearer.Value);
        }
    }
}
