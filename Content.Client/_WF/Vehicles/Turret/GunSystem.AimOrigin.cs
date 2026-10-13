using Content.Shared._WF.Vehicles.Weapons;
using Robust.Shared.Map;

namespace Content.Client.Weapons.Ranged.Systems;

/// <summary>Where a gunner's aim point is measured from.</summary>
public sealed partial class GunSystem
{
    /// <summary>The aim point for a shot at <paramref name="mousePos"/>: from the user, or from the grid under a mounted gun.</summary>
    public EntityCoordinates GetAimCoordinates(EntityUid gunUid, EntityUid user, MapCoordinates mousePos)
    {
        if (!HasComp<GunUseGunOriginComponent>(gunUid))
            return TransformSystem.ToCoordinates(user, mousePos);

        var xform = Transform(gunUid);
        return TransformSystem.ToCoordinates(xform.GridUid ?? xform.MapUid ?? gunUid, mousePos);
    }
}
