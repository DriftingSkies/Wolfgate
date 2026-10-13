using System.Numerics;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Client._WF.Vehicles.Turret;

/// <summary>Where a turret gun's muzzle is drawn right now, from the turret's rendered pose.</summary>
public sealed partial class VehicleTurretMuzzleOffsetSystem : EntitySystem
{
    [Dependency] private GunMuzzleOffsetSystem _gunMuzzleOffset = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VehicleTurretMuzzleSystem _turretMuzzle = default!;
    [Dependency] private VehicleTurretVisualSystem _turretVisual = default!;

    /// <summary>Where a turret gun's muzzle is drawn now.</summary>
    public bool TryGetGunOrigin(EntityUid weaponUid, EntityCoordinates? target, out EntityCoordinates origin)
    {
        return TryGetGunPose(weaponUid, target, out origin, out _);
    }

    /// <summary>Where a turret gun's muzzle is drawn now, and which way it points.</summary>
    public bool TryGetGunPose(
        EntityUid weaponUid,
        EntityCoordinates? target,
        out EntityCoordinates origin,
        out Angle rotation)
    {
        origin = default;
        rotation = Angle.Zero;

        if (!TryComp(weaponUid, out VehicleTurretComponent? turret))
            return false;

        if (!_turretVisual.TryGetRenderedPose(weaponUid, out origin, out rotation))
        {
            origin = _transform.GetMoverCoordinates(weaponUid);
            rotation = _transform.GetWorldRotation(weaponUid);
        }

        EntityCoordinates? aimTarget = target;
        if (aimTarget == null &&
            TryComp(weaponUid, out GunComponent? gun) &&
            gun.ShootCoordinates is { } shootCoordinates)
        {
            aimTarget = shootCoordinates;
        }

        if (TryComp(weaponUid, out GunMuzzleOffsetComponent? gunMuzzle))
        {
            if (_gunMuzzleOffset.TryGetMuzzleCoordinates(weaponUid, gunMuzzle, aimTarget, out var muzzleCoords, out var muzzleRotation))
            {
                origin = muzzleCoords;
                rotation = muzzleRotation;
            }
        }

        if (TryComp(weaponUid, out VehicleTurretMuzzleComponent? turretMuzzle))
            origin = _turretMuzzle.GetMuzzleCoordinates(weaponUid, turretMuzzle, origin);

        return true;
    }
}
