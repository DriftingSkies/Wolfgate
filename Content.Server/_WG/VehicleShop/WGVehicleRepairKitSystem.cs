using Content.Server.Cargo.Systems;
using Content.Shared._WG.VehicleShop;
using Content.Shared._WG.Vehicles.Base;
using Content.Shared._WG.Vehicles.Hardpoint;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Server.Audio;

namespace Content.Server._WG.VehicleShop;

/// <summary>Vehicle repair kits: used on a vehicle, after a while it is whole again and the kit is spent.</summary>
public sealed class WGVehicleRepairKitSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private HardpointSystem _hardpoints = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PricingSystem _pricing = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGVehicleRepairKitComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WGVehicleRepairKitComponent, WGVehicleRepairKitDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(Entity<WGVehicleRepairKitComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } vehicle || !HasComp<RMCVehicleComponent>(vehicle))
            return;

        args.Handled = true;
        if (!CanRepair(ent, vehicle, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason, ("vehicle", vehicle)), vehicle, args.User);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay, new WGVehicleRepairKitDoAfterEvent(), ent, target: vehicle, used: ent)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnDoAfter(Entity<WGVehicleRepairKitComponent> ent, ref WGVehicleRepairKitDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } vehicle)
            return;

        args.Handled = true;
        Apply(ent, vehicle, args.User);
    }

    /// <summary>Whether the kit can fix this vehicle, and the line saying why not.</summary>
    public bool CanRepair(Entity<WGVehicleRepairKitComponent> ent, EntityUid vehicle, out string reason)
    {
        reason = string.Empty;
        if (_hardpoints.GetMissingIntegrity(vehicle, out _) <= 0f)
        {
            reason = "wg-vehicle-repair-kit-intact";
            return false;
        }

        // A vehicle bought at a bargain is as big a job as any other.
        if (_pricing.GetUndiscountedPrice(vehicle, includeContents: false) > ent.Comp.MaxVehicleValue)
        {
            reason = "wg-vehicle-repair-kit-too-big";
            return false;
        }

        return true;
    }

    /// <summary>Makes the vehicle whole and spends the kit. False, with the kit kept, when it can't.</summary>
    public bool Apply(Entity<WGVehicleRepairKitComponent> ent, EntityUid vehicle, EntityUid? user)
    {
        if (!CanRepair(ent, vehicle, out var reason))
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString(reason, ("vehicle", vehicle)), vehicle, user.Value);
            return false;
        }

        _hardpoints.RestoreIntegrity(vehicle);
        if (ent.Comp.Sound != null)
            _audio.PlayPvs(ent.Comp.Sound, vehicle);

        if (user != null)
            _popup.PopupEntity(Loc.GetString("wg-vehicle-repair-kit-done", ("vehicle", vehicle)), vehicle, user.Value);
        QueueDel(ent);
        return true;
    }
}
