using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Doors.Components;
using Content.Shared.Decals;
using Content.Server.Decals;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Server.Parallax;
using Robust.Server.Physics;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;

namespace Content.Server._WF.PlanetPois;

/// <summary>Places reserved sites and their physical roads before terrain generation.</summary>
public sealed partial class WFPlanetPoiSystem
{
    private const int AttemptsPerSite = 128;

    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private GridFixtureSystem _fixtures = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private DecalSystem _decals = default!;

    /// <summary>Builds and validates every site and route before releasing the terrain hold.</summary>
    private async Task PlaceAsync(EntityUid ground, EntityUid orbit, Vector2 centre, float radius, WFPlanetPoiTablePrototype table)
    {
        var record = EnsureComp<WFPlanetPoiComponent>(ground);
        var created = new HashSet<EntityUid>();
        var createdDecals = new List<uint>();
        var changed = new HashSet<Vector2i>();
        var originalTiles = new Dictionary<Vector2i, Tile>();
        var originalRoof = new HashSet<Vector2i>();
        var initialPins = new HashSet<Vector2i>();
        try
        {
            ValidateTable(table);
            if (!TryComp<BiomeComponent>(ground, out var biome) || !TryComp<MapGridComponent>(ground, out var grid))
                throw new InvalidOperationException("Ground has no biome or grid.");

            foreach (var pinned in biome.ModifiedTiles.Values) initialPins.UnionWith(pinned);
            TryComp<RoofComponent>(ground, out var oldRoof);
            var before = _maps.GetAllTilesEnumerator(ground, grid);
            while (before.MoveNext(out var oldTile))
            {
                originalTiles[oldTile.Value.GridIndices] = oldTile.Value.Tile;
                if (oldRoof != null && _roof.IsRooved((ground, grid, oldRoof), oldTile.Value.GridIndices)) originalRoof.Add(oldTile.Value.GridIndices);
            }
            var entries = SelectEntries(table);
            var reserved = new List<Box2i>();
            foreach (var entry in entries)
            {
                if (TerminatingOrDeleted(ground))
                    return;
                var footprint = entry.Footprint ?? new Box2i(-table.Footprint, -table.Footprint, table.Footprint + 1, table.Footprint + 1);
                HashSet<Vector2i> siteTiles;
                Vector2i origin;
                Box2i reservation;
                if (entry.MapPath is { } path)
                {
                    if (!TryOrigin((ground, biome), centre, radius, table, entry, footprint, reserved, out origin))
                        throw new InvalidOperationException($"No room for {entry.Name}: enlarge the world or reduce counts, footprint or clearance.");
                    reservation = Translate(footprint, origin);
                    siteTiles = LoadSite(ground, grid, path, origin, footprint, created, createdDecals, changed);
                }
                else
                {
                    var stagingMap = _maps.CreateMap(out var stagingId, runMapInit: false);
                    var staging = _mapManager.CreateGridEntity(stagingId);
                    staging.Comp.CanSplit = false;
                    try
                    {
                        var config = _proto.Index(entry.Dungeon!.Value);
                        await _dungeon.GenerateDungeonAsync(config, config.ID, staging.Owner, staging.Comp, Vector2i.Zero, _random.Next());
                        if (TerminatingOrDeleted(ground) || TerminatingOrDeleted(staging.Owner)) return;
                        var localTiles = new HashSet<Vector2i>();
                        var generatedTiles = _maps.GetAllTilesEnumerator(staging.Owner, staging.Comp);
                        while (generatedTiles.MoveNext(out var tile)) localTiles.Add(tile.Value.GridIndices);
                        if (localTiles.Count == 0) throw new InvalidOperationException($"{entry.Name} generated no tiles.");
                        var actualFootprint = BoundsOf(localTiles);
                        var clearance = table.Roads is { } accessRoads && entry.Connection != WFPlanetPoiConnection.Isolated ? accessRoads.Width / 2 + accessRoads.ShoulderWidth + 2 : 0;
                        actualFootprint = new Box2i(actualFootprint.Left - clearance, actualFootprint.Bottom - clearance, actualFootprint.Right + clearance, actualFootprint.Top + clearance);
                        footprint = Union(footprint, actualFootprint);
                        if (!TryOrigin((ground, biome), centre, radius, table, entry, footprint, reserved, out origin))
                            throw new InvalidOperationException($"No room for generated {entry.Name}: enlarge the world or reduce counts or clearance.");
                        reservation = Translate(footprint, origin);
                        siteTiles = localTiles.Select(tile => tile + origin).ToHashSet();
                        changed.UnionWith(siteTiles);
                        MergeSite(ground, grid, staging, origin, footprint, created, createdDecals);
                        EnsureComp<RoofComponent>(ground);
                        foreach (var tile in siteTiles) _roof.SetRoof((ground, grid, null), tile, true);
                    }
                    finally { if (!TerminatingOrDeleted(stagingMap)) QueueDel(stagingMap); }
                }
                changed.UnionWith(siteTiles);
                if (siteTiles.Count == 0)
                    throw new InvalidOperationException($"{entry.Name} generated no tiles.");
                var actual = BoundsOf(siteTiles);
                if (!Fits(actual, centre, radius - 1, table.CentreClear) || reserved.Any(other => Overlaps(other, actual)))
                    throw new InvalidOperationException($"{entry.Name} exceeded its reserved placement; increase its footprint or world radius.");
                reserved.Add(Union(reservation, actual));
                if (entry.MapPath != null)
                {
                    var claimed = new HashSet<Vector2i>();
                    for (var x = reservation.Left; x < reservation.Right; x++)
                    for (var y = reservation.Bottom; y < reservation.Top; y++) claimed.Add(new Vector2i(x, y));
                    changed.UnionWith(claimed);
                    _biome.WfPinTiles((ground, biome), claimed);
                }
                else _biome.WfPinTiles((ground, biome), siteTiles);
                var site = new WFPlanetPoiSite
                {
                    Dungeon = entry.Dungeon, MapPath = entry.MapPath, Name = entry.Name,
                    Origin = origin, Bounds = table.Roads != null || entry.MapPath != null ? reservation : actual,
                    TileCount = siteTiles.Count, Connection = entry.Connection,
                };
                if (table.Roads != null && entry.Connection != WFPlanetPoiConnection.Isolated)
                {
                    site.Sockets = ResolveSockets(ground, grid, entry, site, siteTiles, table.Roads, initialPins, centre, radius - 1, table.CentreClear);
                    if (entry.Connection == WFPlanetPoiConnection.Through)
                        ValidateThrough(ground, grid, site, siteTiles);
                }
                site.Signal = SpawnSignal(ground, orbit, new Vector2i((actual.Left + actual.Right) / 2, (actual.Bottom + actual.Top) / 2), entry, table);
                record.Sites.Add(site);
                created.Add(site.Signal);
            }
            if (table.Roads is { } roads)
            {
                if (!float.IsFinite(radius) || radius <= 0 || radius > 4096)
                    throw new InvalidOperationException("Road worlds must have a finite radius of at most 4096 tiles.");
                var blocked = new HashSet<Vector2i>(initialPins);
                var existing = _maps.GetAllTilesEnumerator(ground, grid);
                while (existing.MoveNext(out var tileRef))
                {
                    var tile = tileRef.Value.GridIndices;
                    if (_biome.WfIsPinned((ground, biome), tile) || !Usable(ground, grid, tile))
                        blocked.Add(tile);
                }
                foreach (var site in record.Sites)
                foreach (var socket in site.Sockets)
                {
                    var direction = Direction(socket.Direction);
                    var clearance = roads.Width / 2 + roads.ShoulderWidth;
                    for (var step = 0; step <= clearance + 1; step++)
                    for (var side = -clearance; side <= clearance; side++)
                    {
                        var tile = socket.Position + direction * step + new Vector2i(-direction.Y, direction.X) * side;
                        if (Contains(site.Bounds, tile) && !initialPins.Contains(tile) && (Usable(ground, grid, tile) || _maps.GetTileRef(ground, grid, tile).Tile.IsEmpty))
                            blocked.Remove(tile);
                    }
                }
                var sites = record.Sites.Select(site => new WFPlanetRoadSite(site.Bounds, site.Connection, site.Sockets)).ToList();
                var plan = WFPlanetRoadPlanner.Plan(centre, radius - 1, table.CentreClear, sites, blocked, roads.Width, roads.ShoulderWidth, _random.Next());
                if (!plan.Success)
                    throw new InvalidOperationException(plan.Error ?? "Road routing failed.");
                foreach (var site in record.Sites)
                foreach (var socket in site.Sockets)
                    if (site.AccessPaths.TryGetValue(socket, out var access))
                        plan.RoadTiles.UnionWith(access.Where(tile => _maps.GetTileRef(ground, grid, tile).Tile.IsEmpty));
                changed.UnionWith(plan.ShoulderTiles);
                changed.UnionWith(plan.RoadTiles);
                PaintRoad(ground, grid, biome, plan.ShoulderTiles, roads.ShoulderTile ?? roads.Tile);
                PaintRoad(ground, grid, biome, plan.RoadTiles, roads.Tile);
                record.RoadTiles.UnionWith(plan.RoadTiles);
                record.ShoulderTiles.UnionWith(plan.ShoulderTiles);
            }
            record.NetworkReady = true;
        }
        catch (Exception error)
        {
            if (TerminatingOrDeleted(ground))
                return;
            foreach (var decal in createdDecals) _decals.RemoveDecal(ground, decal);
            foreach (var entity in created)
                if (!TerminatingOrDeleted(entity)) QueueDel(entity);
            if (TryComp<MapGridComponent>(ground, out var rollbackGrid))
            {
                _maps.SetTiles(ground, rollbackGrid, changed.Select(tile => (tile, originalTiles.GetValueOrDefault(tile))).ToList());
                foreach (var tile in changed) _roof.SetRoof((ground, rollbackGrid, null), tile, originalRoof.Contains(tile));
            }
            if (TryComp<BiomeComponent>(ground, out var rollbackBiome))
                foreach (var modified in rollbackBiome.ModifiedTiles.Values) modified.RemoveWhere(tile => changed.Contains(tile) && !initialPins.Contains(tile));
            record.Sites.Clear();
            record.RoadTiles.Clear();
            record.ShoulderTiles.Clear();
            record.GenerationError = $"POI table {table.ID}: {error.Message}";
            Log.Error(record.GenerationError);
            throw;
        }
    }

    private static Box2i Translate(Box2i b, Vector2i o) => new(b.Left + o.X, b.Bottom + o.Y, b.Right + o.X, b.Top + o.Y);
    private static bool Overlaps(Box2i a, Box2i b) => a.Left < b.Right && a.Right > b.Left && a.Bottom < b.Top && a.Top > b.Bottom;
    private static Box2i Union(Box2i a, Box2i b) => new(Math.Min(a.Left, b.Left), Math.Min(a.Bottom, b.Bottom), Math.Max(a.Right, b.Right), Math.Max(a.Top, b.Top));
    private static Box2i BoundsOf(HashSet<Vector2i> tiles) => new(tiles.Min(t => t.X), tiles.Min(t => t.Y), tiles.Max(t => t.X) + 1, tiles.Max(t => t.Y) + 1);
    private static bool Contains(Box2i b, Vector2i t) => t.X >= b.Left && t.X < b.Right && t.Y >= b.Bottom && t.Y < b.Top;

    /// <summary>Keeps the complete footprint within the boundary and outside arrivals.</summary>
    private static bool Fits(Box2i b, Vector2 c, float radius, float clear)
    {
        foreach (var corner in new[] { new Vector2(b.Left, b.Bottom), new Vector2(b.Right, b.Bottom), new Vector2(b.Left, b.Top), new Vector2(b.Right, b.Top) })
            if (Vector2.DistanceSquared(corner, c) >= radius * radius)
                return false;
        var nearest = new Vector2(Math.Clamp(c.X, b.Left, b.Right), Math.Clamp(c.Y, b.Bottom, b.Top));
        return Vector2.DistanceSquared(nearest, c) >= clear * clear;
    }

    /// <summary>Searches a bounded number of origins for a collision-free footprint.</summary>
    private bool TryOrigin(Entity<BiomeComponent> ground, Vector2 centre, float radius, WFPlanetPoiTablePrototype table, WFPlanetPoiEntry entry, Box2i footprint, List<Box2i> reserved, out Vector2i origin)
    {
        origin = default;
        var reach = radius - table.EdgeMargin;
        if (reach <= table.CentreClear)
            return false;
        for (var attempt = 0; attempt < AttemptsPerSite; attempt++)
        {
            var candidate = centre + _random.NextAngle().ToVec() * MathF.Sqrt(_random.NextFloat(table.CentreClear * table.CentreClear, reach * reach));
            origin = new Vector2i((int)MathF.Floor(candidate.X), (int)MathF.Floor(candidate.Y));
            var candidateOrigin = origin;
            var box = Translate(footprint, candidateOrigin);
            if (!Fits(box, centre, radius - 1, table.CentreClear) || reserved.Any(other => Overlaps(other, box) || Vector2.DistanceSquared(new Vector2((other.Left+other.Right)/2f, (other.Bottom+other.Top)/2f), candidateOrigin) < table.Spacing * table.Spacing))
                continue;
            if (entry.MapPath != null && table.Roads is { } roads && entry.Connection != WFPlanetPoiConnection.Isolated
                && entry.Sockets.Any(socket => !ApproachFits(candidateOrigin + socket.Offset, socket.Direction, roads, centre, radius - 1, table.CentreClear,
                    tile => reserved.Any(other => Contains(other, tile)) || _biome.WfIsPinned(ground, tile))))
                continue;
            var pinned = ground.Comp.ModifiedTiles.Values.Any(indices => indices.Any(tile => Contains(box, tile)));
            if (!pinned)
                return true;
        }
        return false;
    }

    /// <summary>Checks complete road and shoulder approaches against bounds and protected terrain.</summary>
    private static bool ApproachFits(Vector2i position, WFPlanetPoiDirection direction, WFPlanetRoadNetwork roads, Vector2 centre, float radius, float centreClear, Func<Vector2i, bool> blocked)
    {
        var vector = Direction(direction);
        var clearance = roads.Width / 2 + roads.ShoulderWidth;
        for (var step = 0; step <= clearance + 1; step++)
        for (var side = -clearance; side <= clearance; side++)
        {
            var tile = position + vector * step + new Vector2i(-vector.Y, vector.X) * side;
            var distance = Vector2.Distance(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), centre);
            if (distance + 0.707107f > radius || distance - 0.707107f < centreClear || blocked(tile)) return false;
        }
        var end = position + vector * (clearance + 1);
        for (var x = -clearance; x <= clearance; x++)
        for (var y = -clearance; y <= clearance; y++)
        {
            var tile = end + new Vector2i(x, y);
            var distance = Vector2.Distance(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), centre);
            if (distance + 0.707107f > radius || distance - 0.707107f < centreClear || blocked(tile)) return false;
        }
        return true;
    }

    /// <summary>Rejects malformed tables before any world mutation.</summary>
    private void ValidateTable(WFPlanetPoiTablePrototype table)
    {
        if (table.MinCount < 0 || table.MaxCount < table.MinCount || table.MaxCount > 128 || table.Entries.Count == 0 || table.Footprint < 1 || table.Footprint > 1024
            || !float.IsFinite(table.Spacing) || table.Spacing < 0 || !float.IsFinite(table.CentreClear) || table.CentreClear < 0
            || !float.IsFinite(table.EdgeMargin) || table.EdgeMargin < 0 || !float.IsFinite(table.RevealRange) || table.RevealRange <= 0)
            throw new InvalidOperationException("Invalid counts, footprint or distances.");
        foreach (var entry in table.Entries)
        {
            if (!float.IsFinite(entry.Weight) || entry.Weight < 0 || entry.MinCount < 0 || entry.MaxCount < entry.MinCount
                || (entry.Dungeon == null) == (entry.MapPath == null) || (entry.Dungeon is { } dungeon && !_proto.HasIndex(dungeon))
                || (entry.MapPath != null && (string.IsNullOrWhiteSpace(entry.MapPath) || entry.Footprint == null)))
                throw new InvalidOperationException($"Invalid entry {entry.Name}: specify one valid dungeon or map, finite weight and bounded counts; maps require a footprint.");
            if (entry.MapPath != null && table.Roads != null && entry.Connection != WFPlanetPoiConnection.Isolated
                && (entry.Sockets.Count < (entry.Connection == WFPlanetPoiConnection.Through ? 2 : 1)
                    || entry.Sockets.Any(socket => !Contains(entry.Footprint!.Value, socket.Offset) || Contains(entry.Footprint.Value, socket.Offset + Direction(socket.Direction)))))
                throw new InvalidOperationException($"Authored {entry.Name} needs outward boundary sockets.");
            if (entry.Footprint is { } box && (box.Right <= box.Left || box.Top <= box.Bottom || box.Right - box.Left > 2048 || box.Top - box.Bottom > 2048))
                throw new InvalidOperationException($"Invalid footprint for {entry.Name}.");
        }
        if (table.Entries.Sum(e => e.MinCount) > table.MaxCount || !table.Entries.Any(e => e.Weight > 0))
            throw new InvalidOperationException("Entry minimums exceed total count or all weights are zero.");
        if (table.Roads is { } roads)
        {
            if (roads.Width < 1 || roads.Width > 15 || roads.Width % 2 == 0 || roads.ShoulderWidth < 0 || roads.ShoulderWidth > 8)
                throw new InvalidOperationException("Invalid road width or shoulder width.");
            _ = _tiles[roads.Tile];
            if (roads.ShoulderTile != null) _ = _tiles[roads.ShoulderTile];
        }
    }

    /// <summary>Honors guaranteed counts and maximums before weighted selection.</summary>
    private List<WFPlanetPoiEntry> SelectEntries(WFPlanetPoiTablePrototype table)
    {
        var result = new List<WFPlanetPoiEntry>();
        foreach (var entry in table.Entries)
            for (var i = 0; i < entry.MinCount; i++) result.Add(entry);
        var wanted = Math.Max(result.Count, _random.Next(table.MinCount, table.MaxCount+1));
        while (result.Count < wanted)
        {
            var eligible = table.Entries.Where(e => e.Weight > 0 && (e.MaxCount == null || result.Count(v => ReferenceEquals(v, e)) < e.MaxCount)).ToList();
            if (eligible.Count == 0) throw new InvalidOperationException("Entry maximums cannot satisfy the requested count.");
            var total = eligible.Sum(e => (double)e.Weight);
            var roll = _random.NextDouble() * total;
            var picked = eligible[^1];
            foreach (var entry in eligible) { roll -= entry.Weight; if (roll <= 0) { picked = entry; break; } }
            result.Add(picked);
        }
        return result.OrderByDescending(e => e.Footprint is { } b ? (long)(b.Right-b.Left)*(b.Top-b.Bottom) : 0).ToList();
    }

    /// <summary>Loads an authored grid in isolation and merges it with its explicit roofs.</summary>
    private HashSet<Vector2i> LoadSite(EntityUid ground, MapGridComponent grid, string path, Vector2i origin, Box2i footprint, HashSet<EntityUid> created, List<uint> createdDecals, HashSet<Vector2i> changed)
    {
        var stagingMap = _maps.CreateMap(out var stagingId, runMapInit: false);
        try
        {
            if (!_loader.TryLoadGrid(stagingId, new ResPath(path), out var loaded))
                throw new InvalidOperationException($"Cannot load single-grid POI map {path}.");
            var source = loaded.Value;
            var result = new HashSet<Vector2i>();
            var roofs = new HashSet<Vector2i>();
            TryComp<RoofComponent>(source.Owner, out var roof);
            var enumerator = _maps.GetAllTilesEnumerator(source.Owner, source.Comp);
            while (enumerator.MoveNext(out var tile))
            {
                var index = tile.Value.GridIndices;
                if (!Contains(footprint, index)) throw new InvalidOperationException($"Map {path} has tiles outside its declared footprint.");
                result.Add(index+origin);
                if (roof != null && _roof.IsRooved((source.Owner, source.Comp, roof), index)) roofs.Add(index+origin);
            }
            changed.UnionWith(result);
            MergeSite(ground, grid, source, origin, footprint, created, createdDecals);
            EnsureComp<RoofComponent>(ground);
            foreach (var tile in result) _roof.SetRoof((ground, grid, null), tile, roofs.Contains(tile));
            return result;
        }
        finally { if (!TerminatingOrDeleted(stagingMap)) QueueDel(stagingMap); }
    }

    /// <summary>Merges tiles, source entities and decals while preserving their local offsets.</summary>
    private void MergeSite(EntityUid ground, MapGridComponent grid, Entity<MapGridComponent> source, Vector2i origin, Box2i footprint, HashSet<EntityUid> created, List<uint> createdDecals)
    {
        var children = Transform(source.Owner).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<MapComponent>(child) || HasComp<MapGridComponent>(child)) continue;
            var local = Transform(child).LocalPosition.Floored();
            if (!Contains(footprint, local) || _maps.GetTileRef(source.Owner, source.Comp, local).Tile.IsEmpty)
                throw new InvalidOperationException($"POI entity {ToPrettyString(child)} is outside its footprint or stands without a source tile.");
        }
        var decals = _decals.GetDecalsIntersecting(source.Owner, source.Comp.LocalAABB.Enlarged(1f));
        var transferred = new HashSet<EntityUid>();
        CaptureChildren(source.Owner, transferred);
        created.UnionWith(transferred);
        // Engine re-anchoring assumes both grids already belong to the same map.
        _transform.SetCoordinates(source.Owner, new EntityCoordinates(ground, Vector2.Zero));
        var canSplit = grid.CanSplit;
        grid.CanSplit = false;
        try { _fixtures.Merge(ground, source.Owner, origin, Angle.Zero, grid, source.Comp); }
        finally { grid.CanSplit = canSplit; }
        var beforeInit = new HashSet<EntityUid>();
        CaptureChildren(ground, beforeInit);
        try
        {
            foreach (var root in transferred)
            {
                if (TerminatingOrDeleted(root)) continue;
                InitializeTransferred(root, _maps.IsInitialized(ground), _maps.IsPaused(ground));
            }
        }
        finally
        {
            var afterInit = new HashSet<EntityUid>();
            CaptureChildren(ground, afterInit);
            afterInit.ExceptWith(beforeInit);
            created.UnionWith(afterInit);
        }
        EnsureComp<DecalGridComponent>(ground);
        foreach (var (_, decal) in decals)
        {
            var position = decal.Coordinates + origin;
            if (!_decals.TryAddDecal(decal.WithCoordinates(position), new EntityCoordinates(ground, position), out var id))
                throw new InvalidOperationException($"Could not transfer POI decal {decal.Id} at {position}.");
            createdDecals.Add(id);
        }
    }

    /// <summary>Runs destination map initialization and pause state through transferred descendants.</summary>
    private void InitializeTransferred(EntityUid root, bool initialized, bool paused)
    {
        var pending = new List<EntityUid> { root };
        for (var i = 0; i < pending.Count; i++)
        {
            var entity = pending[i];
            if (TerminatingOrDeleted(entity)) continue;
            var children = Transform(entity).ChildEnumerator;
            while (children.MoveNext(out var child)) pending.Add(child);
            var meta = MetaData(entity);
            if (initialized && meta.EntityLifeStage != EntityLifeStage.MapInitialized) EntityManager.RunMapInit(entity, meta);
            if (!TerminatingOrDeleted(entity)) _meta.SetEntityPaused(entity, paused);
        }
    }

    /// <summary>Tracks roots transferred by a merge so a rejected generation can remove them.</summary>
    private void CaptureChildren(EntityUid source, HashSet<EntityUid> created)
    {
        var children = Transform(source).ChildEnumerator;
        while (children.MoveNext(out var child)) created.Add(child);
    }

    /// <summary>Resolves usable boundary entrances without demolishing authored entities.</summary>
    private List<WFPlanetRoadSocket> ResolveSockets(EntityUid ground, MapGridComponent grid, WFPlanetPoiEntry entry, WFPlanetPoiSite site, HashSet<Vector2i> tiles, WFPlanetRoadNetwork roads, HashSet<Vector2i> initialPins, Vector2 centre, float radius, float centreClear)
    {
        var sockets = new List<WFPlanetRoadSocket>();
        var otherBounds = Comp<WFPlanetPoiComponent>(ground).Sites.Select(other => other.Bounds).ToArray();
        if (entry.Sockets.Count > 0)
        {
            foreach (var socket in entry.Sockets)
            {
                var position = site.Origin + socket.Offset;
                var direction = Direction(socket.Direction);
                if (!Contains(site.Bounds, position) || Contains(site.Bounds, position+direction) || !tiles.Contains(position) || !Usable(ground, grid, position))
                    throw new InvalidOperationException($"{entry.Name} has an unusable or inward socket at {socket.Offset}.");
                sockets.Add(new WFPlanetRoadSocket(position, socket.Direction));
            }
        }
        else if (entry.MapPath == null)
        {
            var floor = tiles.Where(tile => Usable(ground, grid, tile)).ToHashSet();
            var components = new List<HashSet<Vector2i>>();
            var cardinalDirections = Enum.GetValues<WFPlanetPoiDirection>();
            while (floor.Count > 0)
            {
                var component = new HashSet<Vector2i>();
                var pending = new Queue<Vector2i>();
                pending.Enqueue(floor.First());
                while (pending.TryDequeue(out var current))
                {
                    if (!floor.Remove(current)) continue;
                    component.Add(current);
                    foreach (var cardinal in cardinalDirections)
                        if (floor.Contains(current + Direction(cardinal))) pending.Enqueue(current + Direction(cardinal));
                }
                components.Add(component);
            }
            var totalVisited = 0;
            foreach (var component in components.OrderByDescending(component => component.Count).ThenBy(component => component.Min(tile => tile.X)).ThenBy(component => component.Min(tile => tile.Y)))
            {
                sockets.Clear();
                site.AccessPaths.Clear();
                var frontier = new Queue<Vector2i>();
                var previous = new Dictionary<Vector2i, Vector2i>();
                var reached = new HashSet<Vector2i>(component);
                foreach (var tile in component.OrderBy(tile => tile.X).ThenBy(tile => tile.Y)) frontier.Enqueue(tile);
                var clearance = roads.Width / 2 + roads.ShoulderWidth;
                while (frontier.TryDequeue(out var current))
                {
                    foreach (var cardinal in cardinalDirections)
                    {
                        var direction = Direction(cardinal);
                        var next = current + direction;
                        if (Contains(site.Bounds, next))
                        {
                            if (totalVisited >= 250000 || reached.Contains(next) || initialPins.Contains(next) || !Unblocked(ground, grid, next)) continue;
                            reached.Add(next);
                            totalVisited++;
                            previous[next] = current;
                            frontier.Enqueue(next);
                            continue;
                        }
                        var clear = ApproachFits(current, cardinal, roads, centre, radius, centreClear, tile => initialPins.Contains(tile) || otherBounds.Any(bounds => Contains(bounds, tile)));
                        for (var step = 0; step <= clearance + 1 && clear; step++)
                        for (var side = -clearance; side <= clearance; side++)
                        {
                            var check = current + direction * step + new Vector2i(-direction.Y, direction.X) * side;
                            if (initialPins.Contains(check) || !Unblocked(ground, grid, check)) { clear = false; break; }
                        }
                        if (!clear) continue;
                        var socket = new WFPlanetRoadSocket(current, cardinal);
                        var approach = new List<Vector2i> { current };
                        for (var point = current; previous.TryGetValue(point, out var parent); point = parent) approach.Add(parent);
                        approach.Reverse();
                        sockets.Add(socket);
                        site.AccessPaths[socket] = approach;
                    }
                }

                var required = entry.Connection == WFPlanetPoiConnection.Through ? 2 : 1;
                if (sockets.Select(socket => socket.Position).Distinct().Count() >= required
                    && (required == 1 || sockets.Select(socket => socket.Direction).Distinct().Count() >= 2))
                    break;
                if (totalVisited >= 250000) break;
            }
        }
        sockets = sockets.Distinct().ToList();
        var needed = entry.Connection == WFPlanetPoiConnection.Through ? 2 : 1;
        if (sockets.Select(s => s.Position).Distinct().Count() < needed)
            throw new InvalidOperationException($"{entry.Name} needs {needed} distinct usable boundary exits; no walls will be demolished.");
        if (entry.Connection == WFPlanetPoiConnection.Roadside)
            sockets = sockets.OrderBy(socket => site.AccessPaths.TryGetValue(socket, out var path) ? path.Count : 0).Take(1).ToList();
        if (entry.Connection == WFPlanetPoiConnection.Through && entry.MapPath == null)
        {
            var first = sockets[0];
            var second = sockets.FirstOrDefault(socket => socket.Direction != first.Direction && socket.Position != first.Position);
            if (second == null) throw new InvalidOperationException($"{entry.Name} requires distinct outward exit directions.");
            sockets = new List<WFPlanetRoadSocket> { first, second };
        }
        foreach (var unused in site.AccessPaths.Keys.Where(socket => !sockets.Contains(socket)).ToList())
            site.AccessPaths.Remove(unused);
        return sockets;
    }

    /// <summary>Checks authored through entrances share a usable on-site corridor.</summary>
    private void ValidateThrough(EntityUid ground, MapGridComponent grid, WFPlanetPoiSite site, HashSet<Vector2i> tiles)
    {
        var reachable = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();
        Vector2i Entrance(WFPlanetRoadSocket socket) => site.AccessPaths.TryGetValue(socket, out var access) ? access[0] : socket.Position;
        queue.Enqueue(Entrance(site.Sockets[0]));
        while (queue.TryDequeue(out var current))
        {
            if (!reachable.Add(current)) continue;
            foreach (var direction in Enum.GetValues<WFPlanetPoiDirection>())
            {
                var next = current + Direction(direction);
                if (tiles.Contains(next) && !reachable.Contains(next) && Usable(ground, grid, next)) queue.Enqueue(next);
            }
        }
        if (site.Sockets.Any(socket => !reachable.Contains(Entrance(socket))))
            throw new InvalidOperationException($"{site.Name} through exits do not share a usable internal corridor.");
    }

    /// <summary>Accepts real floor and operable door tiles while rejecting solid obstacles.</summary>
    private bool Usable(EntityUid ground, MapGridComponent grid, Vector2i tile)
    {
        return !_maps.GetTileRef(ground, grid, tile).Tile.IsEmpty && Unblocked(ground, grid, tile);
    }

    /// <summary>Allows empty exterior ground and traversable door tiles, never solid obstacles.</summary>
    private bool Unblocked(EntityUid ground, MapGridComponent grid, Vector2i tile)
    {
        foreach (var entity in _maps.GetAnchoredEntities((ground, grid), tile))
        {
            if (HasComp<DoorComponent>(entity))
            {
                if (TryComp<DoorBoltComponent>(entity, out var bolt) && bolt.BoltsDown) return false;
                continue;
            }
            if (TryComp<PhysicsComponent>(entity, out var body) && body.CanCollide && (body.CollisionLayer & (int)CollisionGroup.MobMask) != 0) return false;
        }
        return true;
    }

    private static Vector2i Direction(WFPlanetPoiDirection direction) => direction switch
    {
        WFPlanetPoiDirection.North => new Vector2i(0,1), WFPlanetPoiDirection.East => new Vector2i(1,0),
        WFPlanetPoiDirection.South => new Vector2i(0,-1), WFPlanetPoiDirection.West => new Vector2i(-1,0),
        _ => throw new InvalidOperationException("Invalid socket direction."),
    };

    /// <summary>Writes and pins the full road batch with open sky.</summary>
    private void PaintRoad(EntityUid ground, MapGridComponent grid, BiomeComponent biome, IEnumerable<Vector2i> positions, string prototype)
    {
        var tiles = positions.ToHashSet();
        var tile = new Tile(_tiles[prototype].TileId);
        _maps.SetTiles(ground, grid, tiles.Select(position => (position, tile)).ToList());
        _biome.WfPinTiles((ground, biome), tiles);
        foreach (var position in tiles) _roof.SetRoof((ground, grid, null), position, false);
    }
}
