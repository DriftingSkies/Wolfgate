using Content.Shared._WG.Vehicles.Base;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared.Buckle.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WG.Tugs;

/// <summary>The tug's seat: the rider drives, the tug never shoves its own rider off, and dismounting puts them on free floor.</summary>
public sealed class WGTugSeatSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private RMCVehicleSystem _vehicles = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VehicleExitSpotSystem _exitSpot = default!;

    /// <summary>How far from the tug a dismounting rider may be put down.</summary>
    private const float DismountReach = 1.5f;

    public override void Initialize()
    {
        SubscribeLocalEvent<WGTugComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<WGTugComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<WGTugComponent, UnstrappedEvent>(OnUnstrapped);
    }

    private void OnPreventCollide(Entity<WGTugComponent> ent, ref PreventCollideEvent args)
    {
        if (TryComp(args.OtherEntity, out BuckleComponent? buckle) && buckle.BuckledTo == ent.Owner)
            args.Cancelled = true;
    }

    private void OnStrapped(Entity<WGTugComponent> ent, ref StrappedEvent args)
    {
        var rider = args.Buckle.Owner;

        // Contacts made while the rider stood on the tug would keep pushing them off the seat.
        _physics.RegenerateContacts(rider);

        if (!_net.IsServer)
            return;

        // Someone the tug ran over before they climbed on is no longer under it.
        if (TryComp(rider, out VehicleRunoverComponent? runover) && runover.Vehicle == ent.Owner)
            RemComp<VehicleRunoverComponent>(rider);

        if (TryComp(ent, out RMCVehicleComponent? vehicle))
            _vehicles.TrySetOperator((ent.Owner, vehicle), rider);
    }

    private void OnUnstrapped(Entity<WGTugComponent> ent, ref UnstrappedEvent args)
    {
        var rider = args.Buckle.Owner;
        _physics.RegenerateContacts(rider);

        if (!_net.IsServer)
            return;

        if (TryComp(ent, out RMCVehicleComponent? vehicle) && vehicle.Operator == rider)
            _vehicles.TryRemoveOperator((ent.Owner, vehicle));

        // Unbuckling leaves the rider standing inside the tug's body, which would shove them out at random. A tug being
        // deleted, or one whose map is going, unseats its rider on the way out and has no floor to offer.
        var xform = Transform(ent);
        if (TerminatingOrDeleted(ent) || TerminatingOrDeleted(rider) || xform.MapUid is not { } map || TerminatingOrDeleted(map) ||
            xform.GridUid is { } grid && TerminatingOrDeleted(grid))
        {
            return;
        }

        var preferred = _transform.GetWorldPosition(rider);
        if (_exitSpot.TryFindConnectedSpot(ent.Owner, rider, preferred, DismountReach, out var spot))
            _transform.SetMapCoordinates(rider, spot);
    }
}
