using Content.Shared._WG.Vehicles.Weapons;

namespace Content.Server._WG.Vehicles.Weapons;

/// <summary>Sets the turret lock from the dashboard button, for the vehicle's weapons operator only.</summary>
public sealed class WGTurretStabilizeSystem : EntitySystem
{
    [Dependency] private VehicleWeaponsSystem _weapons = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGTurretStabilizeRequestEvent>(OnRequest);
    }

    private void OnRequest(WGTurretStabilizeRequestEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user ||
            !TryGetEntity(ev.Vehicle, out var vehicle) ||
            !TryComp(vehicle, out VehicleWeaponsComponent? weapons))
            return;

        _weapons.TrySetStabilization((vehicle.Value, weapons), user, ev.Stabilized);
    }
}
