using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Explosion.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;

namespace Content.Server._WF.Vehicles.Core;

/// <summary>Lets a primed grenade be dropped through a vehicle doorway onto the interior floor.</summary>
public sealed partial class VehicleEntranceItemSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VehicleInteriorSystem _vehicle = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryInsertPrimedGrenade(args.Target, args.User, args.Used);
    }

    private bool TryInsertPrimedGrenade(EntityUid target, EntityUid user, EntityUid used)
    {
        var vehicle = ResolveVehicleTarget(target);
        if (vehicle == EntityUid.Invalid)
            return false;

        if (!TryComp<VehicleEnterComponent>(vehicle, out _))
            return false;

        if (!IsPrimedGrenade(used))
            return false;

        if (TryComp<VehicleLockComponent>(vehicle, out var vehicleLock) &&
            vehicleLock.Locked &&
            !vehicleLock.Broken)
        {
            return false;
        }

        if (!_vehicle.TryFindEntryPoint(vehicle, user, out var entryIndex))
            return false;

        if (!_vehicle.TryGetInteriorEntryCoordinates(vehicle, entryIndex, out var targetCoords))
            return false;

        if (!_hands.TryDrop(user, used, checkActionBlocker: false, doDropInteraction: false))
            return false;

        _transform.SetCoordinates(used, targetCoords);
        return true;
    }

    private EntityUid ResolveVehicleTarget(EntityUid target)
    {
        if (HasComp<VehicleEnterComponent>(target))
            return target;

        if (_vehicle.TryGetVehicleFromInterior(target, out var interiorVehicle) &&
            interiorVehicle is { } vehicle)
        {
            return vehicle;
        }

        return EntityUid.Invalid;
    }

    /// <summary>A hand-held timer item that has been primed; Wolfgate's thrown grenades carry no tag to tell them apart by.</summary>
    private bool IsPrimedGrenade(EntityUid entity)
    {
        return HasComp<OnUseTimerTriggerComponent>(entity) &&
               HasComp<ActiveTimerTriggerComponent>(entity);
    }
}
