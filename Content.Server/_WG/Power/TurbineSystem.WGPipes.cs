using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._FarHorizons.Power.Generation.FissionGenerator;

public sealed partial class TurbineSystem
{
    /// <summary>
    /// Spawns one of the turbine's pipes at an offset from it, parented to the turbine's grid. Parented to the turbine,
    /// a pipe anchors to whichever grid lies under it, which is the ground beneath a landed or crashed ship.
    /// </summary>
    private EntityUid WgSpawnPipe(EntityUid turbine, EntProtoId proto, Vector2 offset, float degrees)
    {
        var at = new EntityCoordinates(turbine, offset);
        if (Transform(turbine).GridUid is not { } grid)
            return SpawnAttachedTo(proto, at, rotation: Angle.FromDegrees(degrees));

        var relative = _transform.GetWorldRotation(turbine) - _transform.GetWorldRotation(grid);
        return SpawnAttachedTo(proto, _transform.WithEntityId(at, grid), rotation: relative + Angle.FromDegrees(degrees));
    }
}
