using Content.Shared._Goobstation.DoAfter;
using Content.Shared._Mono.MotionDetector.Components;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.ProximityDetection;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.ArmorSpecialization;

/// <summary>
/// Vest specializations: fast hands, low signature, unrestricted and second wind. Each reads the wearer's outer
/// clothing slot. Modular plates are Mono's armor plate holder (<c>ClothingArmorPlate</c>).
/// </summary>
public sealed class WFVestSpecializationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
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

        SubscribeLocalEvent<WFSecondWindComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnSecondWindSpeed);

        SubscribeLocalEvent<WFFastHandsComponent, ExaminedEvent>((_, _, args) => args.PushMarkup(Loc.GetString("wf-armor-spec-fast-hands-examine")));
        SubscribeLocalEvent<WFLowSignatureComponent, ExaminedEvent>((_, _, args) => args.PushMarkup(Loc.GetString("wf-armor-spec-low-signature-examine")));
        SubscribeLocalEvent<WFUnrestrictedComponent, ExaminedEvent>((_, _, args) => args.PushMarkup(Loc.GetString("wf-armor-spec-unrestricted-examine")));
        SubscribeLocalEvent<WFSecondWindComponent, ExaminedEvent>((_, _, args) => args.PushMarkup(Loc.GetString("wf-armor-spec-second-wind-examine")));
    }

    private bool TryGetVest<T>(EntityUid wearer, out T comp) where T : IComponent
    {
        comp = default!;
        return _inventory.TryGetSlotEntity(wearer, Slot, out var vest) && TryComp(vest, out comp!);
    }

    private void OnDelayMultiplier(EntityUid uid, InventoryComponent inv, GetDoAfterDelayMultiplierEvent args)
    {
        if (TryGetVest<WFFastHandsComponent>(uid, out var hands))
            args.Multiplier *= hands.Multiplier;
    }

    private void OnProximityDetection(Entity<InventoryComponent> ent, ref ProximityDetectionAttemptEvent args)
    {
        if (HasComp<MotionDetectorIgnoreHolderComponent>(args.Detector.Owner) && TryGetVest<WFLowSignatureComponent>(ent, out _))
            args.Cancel = true;
    }

    private void OnGunRefresh(Entity<InventoryComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!TryGetVest<WFUnrestrictedComponent>(ent, out var free))
            return;

        args.MinAngle *= free.Spread;
        args.MaxAngle *= free.Spread;
        args.AngleIncrease *= free.Spread;
    }

    private void OnSecondWindSpeed(Entity<WFSecondWindComponent> ent, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        if (_timing.CurTime < ent.Comp.ActiveUntil)
            args.Args.ModifySpeed(ent.Comp.SpeedMultiplier, ent.Comp.SpeedMultiplier);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer || _timing.CurTime < _nextCheck)
            return;
        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(0.25);

        var query = EntityQueryEnumerator<WFSecondWindComponent>();
        while (query.MoveNext(out var uid, out var wind))
        {
            if (!_inventory.TryGetContainingEntity(uid, out var wearer) || !TryComp<DamageableComponent>(wearer, out var damage))
                continue;

            if (wind.ActiveUntil != TimeSpan.Zero && _timing.CurTime >= wind.ActiveUntil)
            {
                wind.ActiveUntil = TimeSpan.Zero;
                Dirty(uid, wind);
                _movementSpeed.RefreshMovementSpeedModifiers(wearer.Value);
            }

            if (wind.Spent)
            {
                if (damage.TotalDamage < wind.Threshold / 2)
                {
                    wind.Spent = false;
                    Dirty(uid, wind);
                }
                continue;
            }

            if (damage.TotalDamage < wind.Threshold)
                continue;

            wind.Spent = true;
            wind.ActiveUntil = _timing.CurTime + wind.Duration;
            Dirty(uid, wind);
            _movementSpeed.RefreshMovementSpeedModifiers(wearer.Value);
            _popup.PopupEntity(Loc.GetString("wf-armor-spec-second-wind-popup"), wearer.Value, wearer.Value);
        }
    }
}
