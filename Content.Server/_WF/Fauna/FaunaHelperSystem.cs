using Content.Server.NPC.HTN;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Gibbing.Events;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Shared lookups for fauna systems: the HTN target, free tiles around a spot, kin in range, next-tick spawns and gibs.
/// </summary>
public sealed partial class FaunaHelperSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;

    /// <summary>
    /// The HTN blackboard key that holds the current attack target.
    /// </summary>
    public const string TargetKey = "Target";

    private readonly List<Vector2i> _tiles = new();
    private readonly List<(EntProtoId Proto, MapCoordinates Coords)> _pendingSpawns = new();
    private readonly List<(EntityUid Uid, GibContentsOption Contents)> _pendingGibs = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingSpawns.Count > 0)
        {
            var pending = _pendingSpawns.ToArray();
            _pendingSpawns.Clear();
            foreach (var (proto, coords) in pending)
            {
                if (_map.MapExists(coords.MapId))
                    Spawn(proto, coords);
            }
        }

        if (_pendingGibs.Count == 0)
            return;

        var gibs = _pendingGibs.ToArray();
        _pendingGibs.Clear();
        foreach (var (uid, contents) in gibs)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            if (HasComp<BodyComponent>(uid))
                _body.GibBody(uid, contents: contents);
            else
                QueueDel(uid);
        }
    }

    /// <summary>
    /// Reads the HTN target of the mob, if it has one.
    /// </summary>
    public bool TryGetTarget(EntityUid uid, out EntityUid target)
    {
        target = default;
        return TryComp<HTNComponent>(uid, out var htn) &&
               htn.Blackboard.TryGetValue<EntityUid>(TargetKey, out target, EntityManager);
    }

    /// <summary>
    /// Fills <paramref name="result"/> with the tiles of <paramref name="shape"/> around <paramref name="centre"/>
    /// that have floor, are not blocked and pass <paramref name="predicate"/>.
    /// </summary>
    public void GetFreeTiles(EntityUid grid,
        MapGridComponent gridComp,
        Vector2i centre,
        int range,
        FaunaTileShape shape,
        List<Vector2i> result,
        Predicate<Vector2i>? predicate = null)
    {
        result.Clear();
        for (var x = -range; x <= range; x++)
        {
            for (var y = -range; y <= range; y++)
            {
                if (shape == FaunaTileShape.Ring && Math.Max(Math.Abs(x), Math.Abs(y)) != range ||
                    shape == FaunaTileShape.SquareNoCentre && x == 0 && y == 0)
                    continue;

                var tile = centre + new Vector2i(x, y);
                if (!_map.TryGetTileRef(grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty ||
                    _turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable, gridComp) ||
                    predicate != null && !predicate(tile))
                    continue;

                result.Add(tile);
            }
        }
    }

    /// <summary>
    /// Picks a random tile from <see cref="GetFreeTiles"/>. False when none pass.
    /// </summary>
    public bool TryPickFreeTile(EntityUid grid,
        MapGridComponent gridComp,
        Vector2i centre,
        int range,
        FaunaTileShape shape,
        out Vector2i tile,
        Predicate<Vector2i>? predicate = null)
    {
        GetFreeTiles(grid, gridComp, centre, range, shape, _tiles, predicate);
        if (_tiles.Count == 0)
        {
            tile = default;
            return false;
        }

        tile = _random.Pick(_tiles);
        return true;
    }

    /// <summary>
    /// Fills <paramref name="result"/> with the live mobs of the same prototype in range, other than the mob itself.
    /// </summary>
    public void GetKin<T>(EntityUid uid, float range, HashSet<Entity<T>> result) where T : IComponent
    {
        var proto = MetaData(uid).EntityPrototype;
        result.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, range, result);
        result.RemoveWhere(mate => mate.Owner == uid
                                   || MetaData(mate).EntityPrototype != proto
                                   || !_mobState.IsAlive(mate));
    }

    /// <summary>
    /// Spawns <paramref name="proto"/> at <paramref name="coords"/> next tick, unless the map is gone by then.
    /// </summary>
    public void QueueSpawn(EntProtoId proto, MapCoordinates coords)
    {
        _pendingSpawns.Add((proto, coords));
    }

    /// <summary>
    /// Gibs the mob next tick, or deletes it if it has no body. A death can fire inside another system's query loop,
    /// such as bloodloss in BloodstreamSystem, where gibbing at once would change that loop's collection.
    /// </summary>
    public void QueueGib(EntityUid uid, GibContentsOption contents = GibContentsOption.Drop)
    {
        _pendingGibs.Add((uid, contents));
    }
}

/// <summary>
/// The area <see cref="FaunaHelperSystem.GetFreeTiles"/> searches.
/// </summary>
public enum FaunaTileShape : byte
{
    /// <summary>
    /// Every tile within range, including the centre.
    /// </summary>
    Square,

    /// <summary>
    /// Only the tiles exactly range tiles away.
    /// </summary>
    Ring,

    /// <summary>
    /// Every tile within range except the centre.
    /// </summary>
    SquareNoCentre,
}
