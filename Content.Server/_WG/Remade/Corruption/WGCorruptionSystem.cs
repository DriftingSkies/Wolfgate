using Content.Server._WG.Remade.Counterplay;
using Content.Server._WG.Remade.Monolith;
using Content.Server._WG.Remade.Structures;
using Content.Server.Spreader;
using Content.Shared._WG.Remade;
using Content.Shared.Spreader;
using Content.Shared.Tiles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Remade.Corruption;

/// <summary>
/// Grows corruption out from Monoliths and their nodes, up to a share of the hull set by the stage (plus what nodes feed), and
/// lets it die once the Monolith has gone. The node rules are in the Nodes partial, the carpet and the growth on walls in
/// Smoothing, the way through doors in Doors, and the blood it drinks in Blood.
/// </summary>
public sealed partial class WGCorruptionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>A hard ceiling on the tiles one hull can carry, whatever its size, as a guard against runaway growth.</summary>
    public const int HullCap = 8192;

    private static readonly EntProtoId CorruptionProto = "WGCorruption";
    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(2);
    private const float MaxDecaySeconds = 60f;

    /// <summary>Wall growths are checked in this many slices, one per tick, so a big hull's walls aren't all probed at once.</summary>
    private const int WallSlices = 4;

    private TimeSpan _nextTick;
    private int _wallSlice;

    /// <summary>Tiles with a decay time set, so the tick only looks at dying tiles rather than every one.</summary>
    private readonly HashSet<EntityUid> _decaying = new();
    private readonly List<EntityUid> _decayScratch = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGCorruptionComponent, SpreadNeighborsEvent>(OnSpread);
        SubscribeLocalEvent<WGCorruptionComponent, EntityTerminatingEvent>(OnTerminating);
        InitializeNodes();
        InitializeSanctuary();
    }

    /// <summary>The share of a hull a Monolith may cover at a stage: a quarter at Growth, over half at Harvest, all of it at Rapture, and some more onto docked ships at Crescendo.</summary>
    public static float Share(WGMonolithStage stage)
    {
        return stage switch
        {
            WGMonolithStage.Growth => 0.25f,
            WGMonolithStage.Harvest => 0.55f,
            WGMonolithStage.Rapture => 1f,
            WGMonolithStage.Crescendo => 1.5f,
            _ => 0f,
        };
    }

    /// <summary>
    /// Tiles a Monolith may hold at a stage, from the size of the hull it sits on. A small hull still gets what a small hull
    /// always did, 64, 160 and 256, if that is more than its share.
    /// </summary>
    public static int Limit(WGMonolithStage stage, int hullTiles)
    {
        var share = Share(stage);
        if (share <= 0f)
            return 0;

        var floor = stage switch
        {
            WGMonolithStage.Growth => 64,
            WGMonolithStage.Harvest => 160,
            _ => 256,
        };

        var scaled = (int) MathF.Ceiling(hullTiles * share);
        return Math.Min(HullCap, Math.Max(Math.Min(hullTiles, floor), scaled));
    }

    /// <summary>How much faster than its base chance a tile spreads at a stage: slow while the Monolith is only waking, quick once it is feeding.</summary>
    public static float Pace(WGMonolithStage stage)
    {
        return stage switch
        {
            WGMonolithStage.Growth => 0.5f,
            WGMonolithStage.Harvest => 1f,
            WGMonolithStage.Rapture => 2f,
            WGMonolithStage.Crescendo => 2.5f,
            _ => 1f,
        };
    }

    /// <summary>
    /// Where a Monolith can't reach: inside a sealed hex field, on a grid marked as a sanctuary, and, on a map with no
    /// Lattice, a main station. A grid within the sanctuary radius of a sanctuary station counts too
    /// (<see cref="WGRemadeSanctuaryCVars.Radius"/>), so a Monolith can't sit just off one.
    /// </summary>
    public bool IsSanctuary(EntityUid? grid)
    {
        return grid is { } uid && (HoldsSanctuary(uid) || NearMainStation(uid) || Shielded(uid));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TickRate;
        PruneNearStation();
        PruneHulls();

        var monoliths = EntityQueryEnumerator<WGMonolithComponent, TransformComponent>();
        while (monoliths.MoveNext(out var uid, out var monolith, out var xform))
        {
            Seed((uid, monolith), xform);
        }

        _decayScratch.Clear();
        _decayScratch.AddRange(_decaying);
        foreach (var uid in _decayScratch)
        {
            if (!TryComp<WGCorruptionComponent>(uid, out var tile) || tile.DecayAt is not { } decayAt)
                _decaying.Remove(uid);
            else if (now >= decayAt)
                QueueDel(uid);
        }

        TickWalls();
        TickBlood();
    }

    /// <summary>Tiles a Monolith may hold at its stage: its stage's share of the floor it can reach on its hull.</summary>
    public int MonolithLimit(Entity<WGMonolithComponent> monolith)
    {
        return Limit(monolith.Comp.Stage, ReachTiles(monolith));
    }

    /// <summary>Puts corruption under a Monolith that has reached Growth, once its tile is free of it.</summary>
    private void Seed(Entity<WGMonolithComponent> ent, TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid || IsWarded(gridUid) || GridFull(gridUid) || OwnTiles(ent) >= MonolithLimit(ent))
        {
            return;
        }

        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var attempt = new WGCorruptionGrowAttemptEvent(tile);
        RaiseLocalEvent(gridUid, ref attempt);
        if (attempt.Cancelled)
            return;

        if (!IsCorrupted((gridUid, grid), tile))
            Grow(ent, _map.GridTileToLocal(gridUid, grid, tile), null);
    }

    private void OnSpread(Entity<WGCorruptionComponent> ent, ref SpreadNeighborsEvent args)
    {
        var xform = Transform(ent);
        if (args.NeighborFreeTiles.Count == 0 && !HasDoorway(ent, xform))
        {
            RemCompDeferred<ActiveEdgeSpreaderComponent>(ent);
            return;
        }

        if (ent.Comp.DecayAt != null || !TryComp<WGMonolithComponent>(ent.Comp.Source, out var monolith))
            return;

        if (!_random.Prob(Math.Min(1f, ent.Comp.SpreadChance * Pace(monolith.Stage))))
            return;

        var source = new Entity<WGMonolithComponent>(ent.Comp.Source.Value, monolith);
        foreach (var neighbor in args.NeighborFreeTiles)
        {
            if (IsWarded(neighbor.Tile.GridUid))
                continue;

            if (GridFull(neighbor.Tile.GridUid))
                return;

            var attempt = new WGCorruptionGrowAttemptEvent(neighbor.Tile.GridIndices);
            RaiseLocalEvent(neighbor.Tile.GridUid, ref attempt);
            if (attempt.Cancelled)
                continue;

            var coordinates = _map.GridTileToLocal(neighbor.Tile.GridUid, neighbor.Grid, neighbor.Tile.GridIndices);
            if (!TryPay(source, coordinates, out var node, ent.Comp.Node))
                continue;

            Grow(source, coordinates, node);
            args.Updates--;
            if (args.Updates <= 0)
                return;
        }

        SpreadThroughDoors(ent, source, xform, ref args.Updates);
    }

    /// <summary>Puts down a tile fed by a node, or by the Monolith itself when there is none.</summary>
    private void Grow(Entity<WGMonolithComponent> source, EntityCoordinates coordinates, Entity<WGCorruptionNodeComponent>? node)
    {
        var tile = Spawn(CorruptionProto, coordinates);
        var comp = Comp<WGCorruptionComponent>(tile);
        comp.Source = source;
        comp.Grid = coordinates.EntityId;
        source.Comp.Corruption.Add(tile);
        _gridTiles[coordinates.EntityId] = GridTiles(coordinates.EntityId) + 1;

        if (node is { } feeder)
        {
            comp.Node = feeder.Owner;
            feeder.Comp.Tiles.Add(tile);
        }

        if (TryComp<MapGridComponent>(coordinates.EntityId, out var grid))
            RefreshAround(coordinates.EntityId, _map.CoordinatesToTile(coordinates.EntityId, grid, coordinates), null);
    }

    private void OnTerminating(Entity<WGCorruptionComponent> ent, ref EntityTerminatingEvent args)
    {
        _decaying.Remove(ent);
        if (TryComp<WGMonolithComponent>(ent.Comp.Source, out var monolith))
            monolith.Corruption.Remove(ent);

        if (TryComp<WGCorruptionNodeComponent>(ent.Comp.Node, out var node))
            node.Tiles.Remove(ent);

        if (ent.Comp.Grid is { } grid && _gridTiles.TryGetValue(grid, out var count))
        {
            if (count <= 1)
                _gridTiles.Remove(grid);
            else
                _gridTiles[grid] = count - 1;
        }

        var xform = Transform(ent);
        if (xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var gridComp))
            RefreshAround(gridUid, _map.TileIndicesFor(gridUid, gridComp, xform.Coordinates), ent);
    }

    /// <summary>Starts every tile of a lost Monolith dying, at scattered times.</summary>
    public void StartDecay(WGMonolithComponent monolith)
    {
        var now = _timing.CurTime;
        foreach (var uid in monolith.Corruption)
        {
            if (TryComp<WGCorruptionComponent>(uid, out var tile))
                SetDecay(uid, tile, now + TimeSpan.FromSeconds(_random.NextFloat(0f, MaxDecaySeconds)));
        }
    }

    /// <summary>Sets when a tile dies and tracks it until then.</summary>
    public void SetDecay(EntityUid uid, WGCorruptionComponent tile, TimeSpan at)
    {
        tile.DecayAt = at;
        _decaying.Add(uid);
    }

    public bool IsCorrupted(Entity<MapGridComponent> grid, Vector2i tile)
    {
        return FindCorruption(grid, tile) != null;
    }

    /// <summary>The corruption on a tile, if there is any.</summary>
    public Entity<WGCorruptionComponent>? FindCorruption(Entity<MapGridComponent> grid, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntities(grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (TryComp<WGCorruptionComponent>(uid, out var comp))
                return (uid.Value, comp);
        }

        return null;
    }

    /// <summary>True when the entity stands on a corrupted tile.</summary>
    public bool IsOnCorruption(EntityUid uid, TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref xform, logMissing: false)
            || xform.GridUid is not { } gridUid
            || !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        return IsCorrupted((gridUid, grid), _map.TileIndicesFor(gridUid, grid, xform.Coordinates));
    }
}
