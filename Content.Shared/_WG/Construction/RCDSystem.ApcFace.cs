using Content.Shared._WG.Construction;
using Content.Shared.RCD.Components;

namespace Content.Shared.RCD.Systems;

public partial class RCDSystem
{
    [Dependency] private WGApcFaceSystem _wgApcFace = default!;

    /// <summary>
    /// False, with a popup, when the RCD is set to an APC and one on the tile already faces the way it would be built.
    /// </summary>
    private bool WgApcFaceFree(EntityUid uid, RCDComponent component, MapGridData mapGridData, Direction direction, EntityUid user, bool popMsgs = true)
    {
        var proto = component.CachedPrototype;
        if (proto.Mode != RcdMode.ConstructObject || !_wgApcFace.IsApc(proto.Prototype))
            return true;

        // The facing FinalizeRCDOperation gives the entity.
        var facing = proto.Rotation switch
        {
            RcdRotation.User => direction,
            RcdRotation.Camera => Transform(uid).LocalRotation.GetCardinalDir(),
            _ => Direction.South,
        };

        if (!_wgApcFace.IsTaken((mapGridData.GridUid, mapGridData.Component), mapGridData.Position, facing))
            return true;

        if (popMsgs)
            _popup.PopupClient(Loc.GetString("wg-construction-condition-apc-face-free"), uid, user);

        return false;
    }
}
