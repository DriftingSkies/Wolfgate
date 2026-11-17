using System.Numerics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WG.NetTraffic;

/// <summary>
/// Where each player can see from, as PVS judges it: their body, the entity their eye follows (a vehicle, a ship
/// camera) and any view they are subscribed to.
/// </summary>
public sealed class WGViewPointSystem : EntitySystem
{
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Fills <paramref name="points"/> with every player's view points.</summary>
    public void Collect(List<(ICommonSession Session, MapCoordinates At)> points)
    {
        points.Clear();
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } attached)
                continue;

            Add(session, attached, points);
            if (TryComp<EyeComponent>(attached, out var eye) && eye.Target is { } target && target != attached)
                Add(session, target, points);
            foreach (var view in session.ViewSubscriptions)
            {
                Add(session, view, points);
            }
        }
    }

    /// <summary>The players with a view point within <paramref name="range"/> of a spot.</summary>
    public void InRange(List<(ICommonSession Session, MapCoordinates At)> points, MapCoordinates spot, float range,
        HashSet<ICommonSession> found)
    {
        var rangeSq = range * range;
        foreach (var (session, at) in points)
        {
            if (at.MapId == spot.MapId && (at.Position - spot.Position).LengthSquared() <= rangeSq)
                found.Add(session);
        }
    }

    /// <summary>The players with a view point within <paramref name="range"/> of the segment from a to b.</summary>
    public void NearSegment(List<(ICommonSession Session, MapCoordinates At)> points, MapId map, Vector2 a, Vector2 b,
        float range, HashSet<ICommonSession> found)
    {
        var rangeSq = range * range;
        var ab = b - a;
        var lengthSq = ab.LengthSquared();
        foreach (var (session, at) in points)
        {
            if (at.MapId != map)
                continue;

            var t = lengthSq > 0f ? Math.Clamp(Vector2.Dot(at.Position - a, ab) / lengthSq, 0f, 1f) : 0f;
            if ((a + ab * t - at.Position).LengthSquared() <= rangeSq)
                found.Add(session);
        }
    }

    private void Add(ICommonSession session, EntityUid uid, List<(ICommonSession, MapCoordinates)> points)
    {
        if (!TerminatingOrDeleted(uid))
            points.Add((session, _transform.GetMapCoordinates(uid)));
    }
}
