using Content.Shared._WG.Vehicles.Base;
using Content.Shared._WG.Vehicles.Bison;

namespace Content.Server._WG.Vehicles.Bison;

/// <summary>Sharing the carrier between its cab and its remote: a driver in the seat takes over, and an empty seat hands it back to the remote.</summary>
public sealed partial class CMUCargoVehicleSystem
{
    private readonly HashSet<EntityUid> _retakeWheel = new();

    private void OnVehicleOperatorSet(Entity<CMUCargoVehicleComponent> ent, ref VehicleOperatorSetEvent args)
    {
        // A driver sitting down in the cab cuts the remote link.
        if (args.NewOperator is { } driver && driver != ent.Owner)
        {
            if (TryComp(ent, out CMUCargoVehicleControlSessionComponent? session))
                EndControl((ent.Owner, session), Loc.GetString("cmu-cargo-vehicle-control-ended-driver"));
            return;
        }

        // The driver got up: next tick the carrier is its own operator again, ready for the remote. Not now, inside the
        // event, where the other handlers of this change still have to run.
        if (args.NewOperator == null)
            _retakeWheel.Add(ent.Owner);
    }

    /// <summary>Carriers left without an operator take their own wheel back.</summary>
    private void RetakeWheels()
    {
        if (_retakeWheel.Count == 0)
            return;

        foreach (var uid in _retakeWheel)
        {
            if (TerminatingOrDeleted(uid) ||
                !HasComp<CMUCargoVehicleComponent>(uid) ||
                !TryComp(uid, out RMCVehicleComponent? vehicle) ||
                vehicle.Operator != null)
            {
                continue;
            }

            _vehicles.TrySetOperator((uid, vehicle), uid);
        }

        _retakeWheel.Clear();
    }

    /// <summary>Someone is driving from the cab.</summary>
    private static bool HasCabDriver(EntityUid vehicle, RMCVehicleComponent vehicleBase)
    {
        return vehicleBase.Operator is { } op && op != vehicle;
    }
}
