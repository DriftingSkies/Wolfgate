using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared._Mono.FireControl;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>One planned post: where a crewman of a role stands, and what the planner saw there.</summary>
public readonly record struct WFCrewPost(
    EntityCoordinates Coordinates,
    ProtoId<WFCrewRolePrototype> Role,
    WFCrewPostKind Kind);

public enum WFCrewPostKind : byte
{
    Marker,
    Helm,
    Radio,
    Dock,
    Deck,
    Gunnery,
}

/// <summary>
/// Decides where crew go on a grid. Spawn markers win when the map has any; otherwise a pilot beside the helm, a
/// radio officer beside that, a deckhand inside each airlock and the rest on the most open deck tiles, spread out.
/// Posts keep to breathable deck; a console with none beside it, or a hull with none at all, is manned from open
/// deck, and whoever is posted there is spawned in a void kit.
/// </summary>
public sealed class WFCrewPlannerSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan DeckPostCacheTime = TimeSpan.FromMinutes(5);
    private readonly Dictionary<EntityUid, (TimeSpan Until, List<WFCrewPost> Posts)> _deckPosts = new();
    private TimeSpan _nextPrune;

    /// <summary>Minimum Chebyshev distance in tiles between deck posts and anything else.</summary>
    private const int DeckSpacing = 3;

    private static readonly Vector2i[] Neighbours =
    {
        new(0, -1), new(1, 0), new(0, 1), new(-1, 0),
    };

    /// <summary>How far in tiles from a gunnery console its gunner may be posted when the tiles beside it are taken.</summary>
    private const int StandRadius = 3;

    private static readonly HashSet<Vector2i> NoTiles = new();

    /// <summary>Patrol posts on deck for a grid, replanned at most every five minutes since the tile scan is costly.</summary>
    public IReadOnlyList<WFCrewPost> DeckPosts(EntityUid grid)
    {
        var now = _timing.CurTime;
        if (_deckPosts.TryGetValue(grid, out var cached) && now < cached.Until && !TerminatingOrDeleted(grid))
            return cached.Posts;
        var posts = Plan(grid, 4).Where(post => post.Kind == WFCrewPostKind.Deck).ToList();
        _deckPosts[grid] = (now + DeckPostCacheTime, posts);
        return posts;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_deckPosts.Count == 0 || _timing.CurTime < _nextPrune)
            return;
        _nextPrune = _timing.CurTime + TimeSpan.FromSeconds(30);
        foreach (var (grid, entry) in _deckPosts.ToArray())
        {
            if (TerminatingOrDeleted(grid) || _timing.CurTime >= entry.Until)
                _deckPosts.Remove(grid);
        }
    }

    public List<WFCrewPost> Plan(EntityUid grid, int deckhands = 2)
    {
        var posts = new List<WFCrewPost>();
        if (!TryComp(grid, out MapGridComponent? found))
            return posts;

        // A non-nullable copy for the local function below, which can't see the null check.
        MapGridComponent gridComp = found;

        // Mapper intent wins.
        var hasMarkers = false;
        var markers = EntityQueryEnumerator<WFCrewSpawnPointComponent, TransformComponent>();
        while (markers.MoveNext(out _, out var marker, out var xform))
        {
            if (xform.GridUid != grid)
                continue;
            hasMarkers = true;
            if (IsStandable(grid, _map.TileIndicesFor(grid, gridComp, xform.Coordinates), gridComp))
                posts.Add(new WFCrewPost(xform.Coordinates, marker.Role, WFCrewPostKind.Marker));
        }

        if (hasMarkers)
            return posts;

        var open = FreeTiles(grid, gridComp);
        var free = open.Where(tile => HasSafeAir(grid, tile)).ToHashSet();
        // A hull with no air anywhere is crewed on its open deck.
        if (free.Count == 0)
            free = open;
        var taken = new HashSet<Vector2i>();

        // The helm, and a radio post beside it. Consoles square to the hull come first, as the pilot picks one of them
        // over a turned console however near, so he is posted where he will fly from.
        var helms = new List<(EntityUid Uid, TransformComponent Xform)>();
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var console, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                helms.Add((console, xform));
        }

        foreach (var (_, xform) in helms.OrderByDescending(entry => WFPilotDutySystem.IsSquareHelm(entry.Xform.LocalRotation))
                     .ThenBy(entry => entry.Uid.Id))
        {
            var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            // An open cockpit is flown from the vacuum beside it.
            var deck = free;
            if (!TryNeighbour(tile, deck, taken, out var helm))
            {
                deck = open;
                if (!TryNeighbour(tile, deck, taken, out helm))
                    continue;
            }

            Add(helm, WFCrewRoles.Pilot, WFCrewPostKind.Helm);
            if (TryNeighbour(tile, deck, taken, out var radio))
                Add(radio, WFCrewRoles.RadioOperator, WFCrewPostKind.Radio);
            break;
        }

        var gunnery = EntityQueryEnumerator<FireControlConsoleComponent, TransformComponent>();
        while (gunnery.MoveNext(out _, out _, out var gunneryTransform))
        {
            if (gunneryTransform.GridUid != grid || !gunneryTransform.Anchored)
                continue;
            var tile = _map.TileIndicesFor(grid, gridComp, gunneryTransform.Coordinates);
            // The gunner walks to his console from wherever he is posted, so a ship never goes without one: a console
            // boxed in by the helm's tile takes the nearest free tile, and failing that shares the helm's.
            // A console with no air beside it is not worked from the next room over.
            if (TryNeighbour(tile, free, taken, out var post) || TryNeighbour(tile, open, taken, out post)
                || TryNear(tile, free, taken, out post) || TryNear(tile, free, NoTiles, out post))
                Add(post, WFCrewRoles.Gunner, WFCrewPostKind.Gunnery);
        }

        // The first free tile inside each airlock.
        var docks = EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;

            var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            // A dock mates along its local -Y, so inward is the other way.
            var outward = xform.LocalRotation.RotateVec(new Vector2(0f, -1f));
            var inward = new Vector2i((int) MathF.Round(-outward.X), (int) MathF.Round(-outward.Y));
            for (var i = 1; i <= 3; i++)
            {
                var candidate = new Vector2i(tile.X + inward.X * i, tile.Y + inward.Y * i);
                if (!free.Contains(candidate) || taken.Contains(candidate))
                    continue;

                Add(candidate, WFCrewRoles.Deckhand, WFCrewPostKind.Dock);
                break;
            }
        }

        // Open deck: the most open tiles, kept apart.
        foreach (var tile in free.OrderByDescending(t => Openness(t, free)).ThenBy(t => t.X).ThenBy(t => t.Y))
        {
            if (deckhands <= 0)
                break;

            if (taken.Any(t => Chebyshev(t, tile) < DeckSpacing))
                continue;

            Add(tile, WFCrewRoles.Deckhand, WFCrewPostKind.Deck);
            deckhands--;
        }

        return posts;

        void Add(Vector2i tile, ProtoId<WFCrewRolePrototype> role, WFCrewPostKind kind)
        {
            taken.Add(tile);
            posts.Add(new WFCrewPost(_map.GridTileToLocal(grid, gridComp, tile), role, kind));
        }
    }

    /// <summary>
    /// A cluster of free tiles in the most open part of the deck, which on a hauler is its hold. Used to put cargo
    /// aboard a hull that has no markers for it.
    /// </summary>
    public List<EntityCoordinates> HoldTiles(EntityUid grid, int count)
    {
        var tiles = new List<EntityCoordinates>();
        if (count <= 0 || !TryComp<MapGridComponent>(grid, out var gridComp))
            return tiles;

        var free = FreeTiles(grid, gridComp);
        free.RemoveWhere(tile => !HasSafeAir(grid, tile));
        if (free.Count == 0)
            return tiles;

        var centre = free.OrderByDescending(tile => Openness(tile, free)).ThenBy(tile => tile.X).ThenBy(tile => tile.Y).First();
        foreach (var tile in free.OrderBy(tile => Chebyshev(tile, centre)).ThenBy(tile => tile.X).ThenBy(tile => tile.Y).Take(count))
        {
            tiles.Add(_map.GridTileToLocal(grid, gridComp, tile));
        }

        return tiles;
    }

    /// <summary>Tiles a mob can stand on, whatever the air over them.</summary>
    private HashSet<Vector2i> FreeTiles(EntityUid grid, MapGridComponent gridComp)
    {
        var free = new HashSet<Vector2i>();
        var tiles = _map.GetAllTilesEnumerator(grid, gridComp);
        while (tiles.MoveNext(out var tile))
        {
            if (IsStandable(grid, tile.Value.GridIndices, gridComp))
                free.Add(tile.Value.GridIndices);
        }

        return free;
    }

    /// <summary>Unobstructed flooring, whatever the air over it.</summary>
    public bool IsStandable(EntityUid grid, Vector2i tile, MapGridComponent? gridComp = null)
    {
        return Resolve(grid, ref gridComp) && _map.TryGetTileRef(grid, gridComp, tile, out var turf)
            && !turf.Tile.IsEmpty && !_turf.IsTileBlocked(grid, tile, CollisionGroup.MobMask, gridComp);
    }

    /// <summary>Requires unobstructed flooring and air suitable for crew without sealed internals.</summary>
    public bool IsSafePost(EntityUid grid, Vector2i tile, MapGridComponent? gridComp = null)
    {
        return IsStandable(grid, tile, gridComp) && HasSafeAir(grid, tile);
    }

    /// <summary>Whether the air over a tile can be stood in without a suit.</summary>
    private bool HasSafeAir(EntityUid grid, Vector2i tile)
    {
        var air = _atmos.GetTileMixture(grid, Transform(grid).MapUid, tile);
        if (air == null || !_atmos.IsMixtureProbablySafe(air) || !float.IsFinite(air.Pressure))
            return false;
        return IsBreathable(air, air.Pressure);
    }

    /// <summary>Whether the mix, regulated to the given pressure, has enough oxygen and almost nothing besides nitrogen.</summary>
    public static bool IsBreathable(GasMixture air, float pressure)
    {
        if (air.TotalMoles <= 0 || !float.IsFinite(air.TotalMoles) || !float.IsFinite(pressure))
            return false;
        var pressurePerMole = pressure / air.TotalMoles;
        var oxygen = air.GetMoles(Gas.Oxygen);
        var contaminants = MathF.Max(0, air.TotalMoles - oxygen - air.GetMoles(Gas.Nitrogen));
        return oxygen * pressurePerMole >= 16 && contaminants * pressurePerMole <= 0.1f;
    }

    /// <summary>The free untaken tile nearest to a console within <see cref="StandRadius"/>, the orthogonal ones first.</summary>
    private static bool TryNear(Vector2i tile, HashSet<Vector2i> free, HashSet<Vector2i> taken, out Vector2i found)
    {
        if (TryNeighbour(tile, free, taken, out found))
            return true;

        var best = int.MaxValue;
        for (var x = -StandRadius; x <= StandRadius; x++)
        {
            for (var y = -StandRadius; y <= StandRadius; y++)
            {
                var distance = x * x + y * y;
                var candidate = new Vector2i(tile.X + x, tile.Y + y);
                if (distance == 0 || distance >= best || !free.Contains(candidate) || taken.Contains(candidate))
                    continue;

                best = distance;
                found = candidate;
            }
        }

        return best < int.MaxValue;
    }

    private static bool TryNeighbour(Vector2i tile, HashSet<Vector2i> free, HashSet<Vector2i> taken, out Vector2i found)
    {
        foreach (var dir in Neighbours)
        {
            var candidate = tile + dir;
            if (!free.Contains(candidate) || taken.Contains(candidate))
                continue;

            found = candidate;
            return true;
        }

        found = default;
        return false;
    }

    /// <summary>Free tiles within two of the tile, itself included; 25 is the middle of an open room.</summary>
    private static int Openness(Vector2i tile, HashSet<Vector2i> free)
    {
        var count = 0;
        for (var x = -2; x <= 2; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                if (free.Contains(new Vector2i(tile.X + x, tile.Y + y)))
                    count++;
            }
        }

        return count;
    }

    private static int Chebyshev(Vector2i a, Vector2i b)
    {
        return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}
