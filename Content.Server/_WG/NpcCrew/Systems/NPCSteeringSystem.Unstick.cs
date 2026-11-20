using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.NPC.Systems;

/// <summary>
/// NPC crew who are stuck stop short of a goal someone already stands on, or step aside off the line they were
/// stuck on, since repathing from the same spot returns the same route into the same body.
/// </summary>
public sealed partial class NPCSteeringSystem
{
    /// <summary>How near a goal counts as there when someone else is standing on it.</summary>
    private const float CrewStopShort = 1.5f;

    /// <summary>Sidesteps allowed in <see cref="CrewUnstickWindow"/> before the crewman gives up on the move.</summary>
    private const int CrewUnstickLimit = 3;

    private static readonly TimeSpan CrewUnstickWindow = TimeSpan.FromSeconds(10);

    private Dictionary<EntityUid, (TimeSpan Since, int Count)> _crewUnsticks = new();
    private readonly HashSet<Entity<MobStateComponent>> _crewUnstickMobs = new();

    /// <summary>The tile each sidestepping crewman is headed for.</summary>
    private Dictionary<EntityUid, PathPoly> _crewSidesteps = new();

    /// <summary>Settles or sidesteps a stuck crewman; false leaves him to the usual repath and give-up.</summary>
    private bool TryUnstickCrew(EntityUid uid, NPCSteeringComponent steering, TransformComponent xform, Vector2 direction,
        float targetDistance)
    {
        if (steering.DirectMove || !HasComp<WGCrewComponent>(uid)
            || HasComp<NPCMeleeCombatComponent>(uid) || HasComp<NPCRangedCombatComponent>(uid))
            return false;

        // A crewman already holding the spot: the goal is a post or patrol point, not the patient or quarry under him.
        if (targetDistance <= steering.Range + CrewStopShort && IsMobAt(uid, steering.Coordinates, crewOnly: true))
        {
            steering.Status = SteeringStatus.InRange;
            ResetStuck(steering, xform.Coordinates);
            return true;
        }

        var now = _timing.CurTime;
        if (_crewUnsticks.TryGetValue(uid, out var tries) && now - tries.Since < CrewUnstickWindow)
        {
            if (tries.Count >= CrewUnstickLimit)
                return false;
        }
        else
        {
            tries = (now, 0);
        }

        // A path already on its way would land over the sidestep.
        if (steering.Pathfind || xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp)
            || direction.LengthSquared() < 0.0001f)
            return false;

        // The direction is a world one; tiles are counted in the grid's own frame.
        var ahead = Vector2.Normalize((-_transform.GetWorldRotation(grid)).RotateVec(direction));
        var right = new Vector2(ahead.Y, -ahead.X);
        var mine = _mapSystem.TileIndicesFor(grid, gridComp, xform.Coordinates);
        foreach (var step in new[] { right, -right, right - ahead, -right - ahead, -ahead })
        {
            var offset = new Vector2i((int) MathF.Round(step.X), (int) MathF.Round(step.Y));
            if (offset == Vector2i.Zero)
                continue;

            var tile = mine + offset;
            if (!_mapSystem.TryGetTileRef(grid, gridComp, tile, out var turf) || turf.Tile.IsEmpty
                || _crewTurf.IsTileBlocked(grid, tile, CollisionGroup.MobMask, gridComp))
                continue;

            var centre = _mapSystem.GridTileToLocal(grid, gridComp, tile);
            if (IsMobAt(uid, centre) || _pathfindingSystem.GetPoly(centre) is not { } poly)
                continue;

            steering.CurrentPath.Clear();
            steering.CurrentPath.Enqueue(poly);
            ResetStuck(steering, xform.Coordinates);
            PruneCrewUnsticks(now);
            _crewUnsticks[uid] = (tries.Since, tries.Count + 1);
            _crewSidesteps[uid] = poly;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Drops what no longer counts, once the tables grow: unstick tallies past their window, and sidesteps whose crewman
    /// is gone or no longer heading for that tile. A crewman still on his way keeps his waypoint.
    /// </summary>
    internal void PruneCrewUnsticks(TimeSpan now)
    {
        if (_crewUnsticks.Count > 64)
        {
            foreach (var (other, tally) in _crewUnsticks)
            {
                if (now - tally.Since >= CrewUnstickWindow)
                    _crewUnsticks.Remove(other);
            }
        }

        if (_crewSidesteps.Count <= 64)
            return;

        foreach (var (other, waypoint) in _crewSidesteps)
        {
            if (!TryComp<NPCSteeringComponent>(other, out var steering) || steering.CurrentPath.Count == 0
                || steering.CurrentPath.Peek() != waypoint)
                _crewSidesteps.Remove(other);
        }
    }

    /// <summary>Records a crewman's sidestep waypoint, as a sidestep does (for tests).</summary>
    internal void TrackCrewSidestep(EntityUid uid, PathPoly waypoint)
    {
        _crewSidesteps[uid] = waypoint;
    }

    /// <summary>
    /// Swaps in empty unstick tables and hands back the live ones, for a test to work on tables of its own; it gives
    /// them back with <see cref="RestoreCrewUnsticks"/>.
    /// </summary>
    internal (Dictionary<EntityUid, (TimeSpan Since, int Count)> Unsticks, Dictionary<EntityUid, PathPoly> Sidesteps)
        IsolateCrewUnsticks()
    {
        var live = (_crewUnsticks, _crewSidesteps);
        _crewUnsticks = new();
        _crewSidesteps = new();
        return live;
    }

    /// <summary>Puts back the tables <see cref="IsolateCrewUnsticks"/> took (for tests).</summary>
    internal void RestoreCrewUnsticks(
        (Dictionary<EntityUid, (TimeSpan Since, int Count)> Unsticks, Dictionary<EntityUid, PathPoly> Sidesteps) live)
    {
        _crewUnsticks = live.Unsticks;
        _crewSidesteps = live.Sidesteps;
    }

    /// <summary>Whether a crewman's sidestep waypoint is still kept (for tests).</summary>
    internal bool HasCrewSidestep(EntityUid uid)
    {
        return _crewSidesteps.ContainsKey(uid);
    }

    /// <summary>How many sidestep waypoints are kept (for tests).</summary>
    internal int CrewSidestepCount => _crewSidesteps.Count;

    /// <summary>
    /// Whether a crewman is still on his way to the tile he stepped aside to: until he reaches it, no new path is
    /// asked for, as one from where he stands would run back into the body he stepped round.
    /// </summary>
    private bool IsCrewSidestepping(EntityUid uid, NPCSteeringComponent steering)
    {
        if (!_crewSidesteps.TryGetValue(uid, out var poly))
            return false;

        // A fight takes over his steering: the waypoint no longer holds his path back.
        if (steering.CurrentPath.Count > 0 && steering.CurrentPath.Peek() == poly
            && !HasComp<NPCRangedCombatComponent>(uid) && !HasComp<NPCMeleeCombatComponent>(uid))
            return true;

        _crewSidesteps.Remove(uid);
        return false;
    }

    /// <summary>Whether another mob stands at a spot; with <paramref name="crewOnly"/>, a crewman on his feet.</summary>
    private bool IsMobAt(EntityUid uid, EntityCoordinates coordinates, bool crewOnly = false)
    {
        _crewUnstickMobs.Clear();
        _lookup.GetEntitiesInRange(coordinates, 0.6f, _crewUnstickMobs);
        foreach (var mob in _crewUnstickMobs)
        {
            if (mob.Owner != uid && (!crewOnly || HasComp<WGCrewComponent>(mob) && mob.Comp.CurrentState == MobState.Alive))
                return true;
        }

        return false;
    }
}
