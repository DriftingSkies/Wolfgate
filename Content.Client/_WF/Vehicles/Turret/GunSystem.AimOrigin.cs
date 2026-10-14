using System.Numerics;
using Content.Client._WF.Vehicles.Turret;
using Content.Shared._WF.Vehicles.Weapons;
using Robust.Shared.Map;

namespace Content.Client.Weapons.Ranged.Systems;

/// <summary>Where a gunner's aim point is measured from, and where a mounted gun's flash is drawn.</summary>
public sealed partial class GunSystem
{
    [Dependency] private GunMuzzleOffsetSystem _wfMuzzleOffset = default!;
    [Dependency] private VehicleTurretMuzzleOffsetSystem _wfTurretMuzzle = default!;

    /// <summary>
    /// Where a mounted gun's muzzle is from the gun itself, in the flash's own axes; a flash follows its gun at this
    /// offset. Null for a gun with no muzzle of its own to find.
    /// </summary>
    private Vector2? GetMountedFlashOffset(EntityUid gunUid, TransformComponent gunXform, Angle flashAngle)
    {
        EntityCoordinates muzzle;
        if (!_wfTurretMuzzle.TryGetGunOrigin(gunUid, null, out muzzle) &&
            (!TryComp(gunUid, out GunMuzzleOffsetComponent? offset) ||
             !_wfMuzzleOffset.TryGetMuzzleCoordinates(gunUid, offset, null, out muzzle, out _)))
        {
            return null;
        }

        var muzzleMap = TransformSystem.ToMapCoordinates(muzzle);
        if (muzzleMap.MapId != gunXform.MapID)
            return null;

        return (-flashAngle).RotateVec(muzzleMap.Position - TransformSystem.GetWorldPosition(gunXform));
    }

    /// <summary>The aim point for a shot at <paramref name="mousePos"/>: from the user, or from the grid under a mounted gun.</summary>
    public EntityCoordinates GetAimCoordinates(EntityUid gunUid, EntityUid user, MapCoordinates mousePos)
    {
        if (!HasComp<GunUseGunOriginComponent>(gunUid))
            return TransformSystem.ToCoordinates(user, mousePos);

        var xform = Transform(gunUid);
        return TransformSystem.ToCoordinates(xform.GridUid ?? xform.MapUid ?? gunUid, mousePos);
    }
}
