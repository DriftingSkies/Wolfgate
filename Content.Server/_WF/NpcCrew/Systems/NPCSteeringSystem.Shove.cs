using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Map.Components;

namespace Content.Server.NPC.Systems;

/// <summary>Lets crew shove a loose crate standing on their path onto a free tile beside it.</summary>
public sealed partial class NPCSteeringSystem
{
    [Dependency] private PullingSystem _crewPulling = default!;
    [Dependency] private SharedPopupSystem _crewPopup = default!;
    [Dependency] private TurfSystem _crewTurf = default!;

    /// <summary>How long a crewman braces against a crate before it gives.</summary>
    private static readonly TimeSpan ShoveTime = TimeSpan.FromSeconds(1);

    /// <summary>A brace older than this was left off and starts over.</summary>
    private static readonly TimeSpan ShoveForgotten = TimeSpan.FromSeconds(5);

    private static readonly Vector2i[] ShoveOffsets =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    private readonly Dictionary<EntityUid, TimeSpan> _shoveStarted = new();
    private readonly List<EntityUid> _shoved = new();
    private readonly HashSet<Vector2i> _shovePath = new();

    /// <summary>Whether a loose crate on a path node is in a shoving NPC's way, so the node is handled rather than walked into.</summary>
    private bool IsCrewShoveBlocked(EntityUid uid, NPCSteeringComponent steering, PathPoly node)
    {
        if (!CanShove(steering, node))
            return false;

        FindShovable(uid, node);
        return _shoved.Count > 0;
    }

    /// <summary>Braces against the crates on a node, then shoves each to the side; null when the node is not the NPC's to shove.</summary>
    private SteeringObstacleStatus? TryShoveCrewObstacle(EntityUid uid, NPCSteeringComponent steering, PathPoly node)
    {
        if (!CanShove(steering, node))
            return null;

        FindShovable(uid, node);
        if (_shoved.Count == 0)
        {
            _shoveStarted.Remove(uid);
            return SteeringObstacleStatus.Completed;
        }

        var now = _timing.CurTime;
        if (!_shoveStarted.TryGetValue(uid, out var started) || now - started > ShoveForgotten)
        {
            // Braces left off by NPCs that died, repathed or stopped shoving are dropped as new ones start.
            foreach (var (other, since) in _shoveStarted)
            {
                if (now - since > ShoveForgotten)
                    _shoveStarted.Remove(other);
            }

            _shoveStarted[uid] = now;
            return SteeringObstacleStatus.Continuing;
        }

        if (now - started < ShoveTime)
            return SteeringObstacleStatus.Continuing;

        _shoveStarted.Remove(uid);
        foreach (var crate in _shoved)
        {
            if (!TryShove(uid, steering, crate))
                return SteeringObstacleStatus.Failed;
        }

        return SteeringObstacleStatus.Completed;
    }

    private static bool CanShove(NPCSteeringComponent steering, PathPoly node)
    {
        return (steering.Flags & PathFlags.Shoving) != 0x0 && (node.Data.Flags & PathfindingBreadcrumbFlag.Movable) != 0x0;
    }

    /// <summary>Fills <see cref="_shoved"/> with the loose crates on a node that the NPC would bump into.</summary>
    private void FindShovable(EntityUid uid, PathPoly node)
    {
        _shoved.Clear();
        var ents = _entSetPool.Get();
        _lookup.GetLocalEntitiesIntersecting(node.GraphUid, node.Box.Enlarged(-0.04f), ents, flags: LookupFlags.Dynamic);
        foreach (var ent in ents)
        {
            if (ent != uid && _xformQuery.TryGetComponent(ent, out var xform) && _pathfindingSystem.IsShovable(ent, xform)
                && IsCrewObstacleCollidable(uid, ent))
                _shoved.Add(ent);
        }

        _entSetPool.Return(ents);
    }

    /// <summary>
    /// Moves a crate one tile off the path, to the side rather than ahead and never back onto the shover, onto open
    /// deck with no door. False when there is nowhere to put it.
    /// </summary>
    private bool TryShove(EntityUid uid, NPCSteeringComponent steering, EntityUid crate)
    {
        var xform = Transform(crate);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var tile = _mapSystem.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var mine = _mapSystem.TileIndicesFor(grid, gridComp, Transform(uid).Coordinates);
        var ahead = (Vector2) (tile - mine);
        ahead = ahead == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(ahead);

        _shovePath.Clear();
        foreach (var poly in steering.CurrentPath)
        {
            if (poly.GraphUid != grid)
                continue;

            var centre = poly.Box.Center;
            _shovePath.Add(new Vector2i((int) MathF.Floor(centre.X), (int) MathF.Floor(centre.Y)));
        }

        Vector2i? best = null;
        var bestScore = float.MaxValue;
        foreach (var offset in ShoveOffsets)
        {
            var to = tile + offset;
            if (to == mine || _shovePath.Contains(to))
                continue;

            var along = Vector2.Dot(Vector2.Normalize((Vector2) offset), ahead);
            if (along < -0.1f)
                continue;

            var score = MathF.Abs(along) + (offset.X != 0 && offset.Y != 0 ? 0.1f : 0f);
            if (score >= bestScore || !_mapSystem.TryGetTileRef(grid, gridComp, to, out var turf) || turf.Tile.IsEmpty
                || _crewTurf.IsTileBlocked(grid, to, CollisionGroup.MobMask, gridComp) || HasDoor(grid, gridComp, to))
                continue;

            best = to;
            bestScore = score;
        }

        if (best is not { } target)
            return false;

        if (TryComp<PullableComponent>(crate, out var pullable) && pullable.BeingPulled)
            _crewPulling.TryStopPull(crate, pullable);

        _transform.SetCoordinates(crate, _mapSystem.GridTileToLocal(grid, gridComp, target));
        _crewPopup.PopupEntity(Loc.GetString("wf-crew-shove", ("user", uid), ("crate", crate)), crate);
        return true;
    }

    private bool HasDoor(EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(grid, gridComp, tile);
        while (anchored.MoveNext(out var ent))
        {
            if (HasComp<DoorComponent>(ent))
                return true;
        }

        return false;
    }
}
