using Content.Server.Pointing.Components;
using Content.Shared.IdentityManagement;
using Robust.Shared.Player;

namespace Content.Server.Pointing.EntitySystems;

internal sealed partial class PointingSystem
{
    /// <summary>
    /// An NPC points at someone as a player would: the arrow over them and the popups to everyone near. No session, so
    /// no per-player cooldown; the caller keeps its own.
    /// </summary>
    public bool TryNpcPoint(EntityUid pointer, EntityUid pointed)
    {
        if (!Exists(pointed) || HasComp<PointingArrowComponent>(pointed) || !CanPoint(pointer))
            return false;

        var coords = Transform(pointed).Coordinates;
        if (!InRange(pointer, coords))
            return false;

        _rotateToFaceSystem.TryFaceCoordinates(pointer, _transform.ToMapCoordinates(coords).Position);
        var arrow = Spawn("PointingArrow", coords);
        if (TryComp<PointingArrowComponent>(arrow, out var pointing))
        {
            pointing.StartPosition = _transform.ToCoordinates((arrow, Transform(arrow)),
                _transform.ToMapCoordinates(Transform(pointer).Coordinates)).Position;
            pointing.EndTime = _gameTiming.CurTime + PointDuration;
            Dirty(arrow, pointing);
        }

        var pointerName = Identity.Entity(pointer, EntityManager);
        var pointedName = Identity.Entity(pointed, EntityManager);
        var from = _transform.GetMapCoordinates(pointer);
        var viewers = Filter.Empty()
            .AddWhere(s => s.AttachedEntity is { Valid: true } ent && _transform.GetMapCoordinates(ent).InRange(from, PointingRange))
            .Recipients;
        SendMessage(pointer, viewers, pointed,
            Loc.GetString("pointing-system-point-at-other", ("other", pointedName)),
            Loc.GetString("pointing-system-point-at-other-others", ("otherName", pointerName), ("other", pointedName)),
            Loc.GetString("pointing-system-point-at-you-other", ("otherName", pointerName)));
        return true;
    }
}
