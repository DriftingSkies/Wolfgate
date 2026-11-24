using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared._WG.MappingMode;
using Content.Shared._WG.MappingMode.Issues;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.MappingMode.Issues;

/// <summary>The rules that hold for any level: what the map file won't keep and what is placed wrong.</summary>
public sealed partial class WGMappingIssuesSystem
{
    [Dependency] private TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    /// <summary>Checks one level and collects its grids with their depth.</summary>
    private void CheckLevel(Report report, EntityUid level, int depth, WGMappingIssueScope scope,
        List<(EntityUid Grid, int Depth)> grids)
    {
        var before = grids.Count;
        var children = Transform(level).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<WGMappingObserverComponent>(child))
                continue;

            if (TryComp<MapGridComponent>(child, out var grid))
            {
                grids.Add((child, depth));
                CheckGrid(report, (child, grid), depth, scope);
                continue;
            }

            // Anything else under the map is loose in space; the ship file never holds it and a map file keeps
            // it floating.
            Walk(child, uid => CheckEntity(report, uid, depth, scope));
            Add(report, WGMappingIssueCatalog.MapLooseEntity, child, Transform(child).Coordinates, depth,
                ("name", NameOf(child)));
        }

        // A ship check reports the grid count itself.
        if (grids.Count == before && scope != WGMappingIssueScope.Ship)
            Add(report, WGMappingIssueCatalog.MapLevelEmpty, null, null, depth, ("depth", depth));
    }

    /// <summary>Scans a grid and checks its anchored entities tile by tile and every entity it holds.</summary>
    private void CheckGrid(Report report, Entity<MapGridComponent> grid, int depth, WGMappingIssueScope scope)
    {
        var scan = _vessel.Scan(grid);
        report.Grids++;
        report.Tiles += scan.Tiles;
        report.Appraisal += double.IsFinite(scan.Appraisal) ? scan.Appraisal : 0;

        // Anchored entities by tile and kind: two of one kind on a tile are a doubled wall or cable. Pipes go by layer
        // instead, since differently shaped pipes still clash where their openings overlap.
        var kinds = new Dictionary<(Vector2i, string), (EntityUid First, int Count)>();
        var pipes = new Dictionary<(Vector2i, AtmosPipeLayer), List<(EntityUid Owner, PipeDirection Sides)>>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            Walk(child, uid => CheckEntity(report, uid, depth, scope));

            var xform = Transform(child);
            if (!xform.Anchored)
                continue;

            var indices = _map.TileIndicesFor(grid, grid.Comp, xform.Coordinates);
            if (!_map.TryGetTileRef(grid, grid.Comp, indices, out var tile) || tile.Tile.IsEmpty)
            {
                Add(report, WGMappingIssueCatalog.MapAnchoredNoTile, child, xform.Coordinates, depth,
                    ("name", NameOf(child)));
            }

            if (CollectPipes(child, indices, pipes))
                continue;

            if (KindOf(child, xform) is not { } kind)
                continue;

            var key = (indices, kind);
            kinds[key] = kinds.TryGetValue(key, out var entry) ? (entry.First, entry.Count + 1) : (child, 1);
        }

        foreach (var ((indices, _), (first, count)) in kinds)
        {
            if (count >= 2)
                AddDoubled(report, grid, indices, first, count, depth);
        }

        foreach (var ((indices, _), onLayer) in pipes)
        {
            if (ClashingPipes(onLayer) is { } clash)
                AddDoubled(report, grid, indices, clash.First, clash.Count, depth);
        }
    }

    private void AddDoubled(Report report, EntityUid grid, Vector2i indices, EntityUid first, int count, int depth)
    {
        var center = new EntityCoordinates(grid, indices.X + 0.5f, indices.Y + 0.5f);
        Add(report, WGMappingIssueCatalog.MapDoubled, first, center, depth, ("name", NameOf(first)), ("count", count));
    }

    /// <summary>
    /// What makes two anchored entities doubles: any two walls, cables of one voltage, or the same prototype facing
    /// the same way (directional windows on different edges of a tile are fine).
    /// </summary>
    private string? KindOf(EntityUid uid, TransformComponent xform)
    {
        if (TryComp<CableComponent>(uid, out var cable))
            return $"cable:{cable.CableType}";

        if (_tag.HasTag(uid, WallTag))
            return "wall";

        return MetaData(uid).EntityPrototype is { } prototype
            ? $"{prototype.ID}:{xform.LocalRotation.GetCardinalDir()}"
            : null;
    }

    /// <summary>Files an entity's pipe openings by tile and layer; false when it has no pipe node.</summary>
    private bool CollectPipes(EntityUid uid, Vector2i indices,
        Dictionary<(Vector2i, AtmosPipeLayer), List<(EntityUid Owner, PipeDirection Sides)>> pipes)
    {
        if (!TryComp<NodeContainerComponent>(uid, out var container))
            return false;

        var found = false;
        foreach (var node in container.Nodes.Values)
        {
            if (node is not PipeNode { NodeGroupID: NodeGroupID.Pipe } pipe)
                continue;

            found = true;
            var key = (indices, pipe.CurrentPipeLayer);
            if (!pipes.TryGetValue(key, out var onLayer))
                pipes[key] = onLayer = new List<(EntityUid, PipeDirection)>();

            // From the rotation itself: a pipe with connections off keeps stale directions after a mapping-tool turn.
            var sides = pipe.RotationsEnabled
                ? pipe.OriginalPipeDirection.RotatePipeDirection(Transform(uid).LocalRotation)
                : pipe.OriginalPipeDirection;
            onLayer.Add((uid, sides));
        }

        return found;
    }

    /// <summary>The pipes on one tile and layer whose openings overlap another entity's, or null when none do.</summary>
    private static (EntityUid First, int Count)? ClashingPipes(List<(EntityUid Owner, PipeDirection Sides)> onLayer)
    {
        EntityUid? first = null;
        var clashing = new HashSet<EntityUid>();
        for (var i = 0; i < onLayer.Count; i++)
        {
            for (var j = i + 1; j < onLayer.Count; j++)
            {
                var (a, aSides) = onLayer[i];
                var (b, bSides) = onLayer[j];
                if (a == b || (aSides & bSides) == PipeDirection.None)
                    continue;

                first ??= a;
                clashing.Add(a);
                clashing.Add(b);
            }
        }

        return first is { } uid ? (uid, clashing.Count) : null;
    }

    /// <summary>The per-entity rules: a prototype the map file drops, one the map lint refuses, and a denied component.</summary>
    private void CheckEntity(Report report, EntityUid uid, int depth, WGMappingIssueScope scope)
    {
        report.Entities++;
        var coords = Transform(uid).Coordinates;
        if (MetaData(uid).EntityPrototype is { } prototype && WGMappingSpawnerIndex.WontSave(prototype))
        {
            var spawner = _mapping.SpawnerIndex.TryGetSpawner(prototype.ID, out var spawnerId) &&
                          _prototypes.TryIndex<EntityPrototype>(spawnerId, out var spawnerProto)
                ? string.IsNullOrEmpty(spawnerProto.Name) ? spawnerProto.ID : spawnerProto.Name
                : "none";
            Add(report, WGMappingIssueCatalog.MapUnsaved, uid, coords, depth,
                ("name", NameOf(uid)), ("spawner", spawner));
        }

        if (MetaData(uid).EntityPrototype is { } linted && WGMappingMapLint.IsIllegal(linted, scope == WGMappingIssueScope.Grid))
            Add(report, WGMappingIssueCatalog.MapLint, uid, coords, depth, ("name", NameOf(uid)), ("id", linted.ID));

        if (_mapping.HasDeniedComponent(uid))
            Add(report, WGMappingIssueCatalog.MapDenied, uid, coords, depth, ("name", NameOf(uid)));
    }

    /// <summary>Visits an entity and everything parented to it, containers included, observers left out.</summary>
    private void Walk(EntityUid root, Action<EntityUid> visit)
    {
        var stack = new Stack<EntityUid>();
        stack.Push(root);
        while (stack.TryPop(out var uid))
        {
            if (HasComp<WGMappingObserverComponent>(uid))
                continue;

            visit(uid);
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                stack.Push(child);
            }
        }
    }
}
