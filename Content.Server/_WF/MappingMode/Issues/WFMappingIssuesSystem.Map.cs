using Content.Server.Power.Components;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Issues;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingMode.Issues;

/// <summary>The rules that hold for any level: what the map file won't keep and what is placed wrong.</summary>
public sealed partial class WFMappingIssuesSystem
{
    /// <summary>Checks one level and collects its grids with their depth.</summary>
    private void CheckLevel(Report report, EntityUid level, int depth, WFMappingIssueScope scope,
        List<(EntityUid Grid, int Depth)> grids)
    {
        var before = grids.Count;
        var children = Transform(level).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<WFMappingObserverComponent>(child))
                continue;

            if (TryComp<MapGridComponent>(child, out var grid))
            {
                grids.Add((child, depth));
                CheckGrid(report, (child, grid), depth);
                continue;
            }

            // Anything else under the map is loose in space; the ship file never holds it and a map file keeps
            // it floating.
            Walk(child, uid => CheckEntity(report, uid, depth));
            Add(report, WFMappingIssueCatalog.MapLooseEntity, child, Transform(child).Coordinates, depth,
                ("name", NameOf(child)));
        }

        // A ship check reports the grid count itself.
        if (grids.Count == before && scope == WFMappingIssueScope.Map)
            Add(report, WFMappingIssueCatalog.MapLevelEmpty, null, null, depth, ("depth", depth));
    }

    /// <summary>Scans a grid and checks its anchored entities tile by tile and every entity it holds.</summary>
    private void CheckGrid(Report report, Entity<MapGridComponent> grid, int depth)
    {
        var scan = _vessel.Scan(grid);
        report.Grids++;
        report.Tiles += scan.Tiles;
        report.Appraisal += double.IsFinite(scan.Appraisal) ? scan.Appraisal : 0;

        // Anchored entities by tile and kind: two of one kind on a tile are a doubled wall or cable.
        var kinds = new Dictionary<(Vector2i, string), (EntityUid First, int Count)>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            Walk(child, uid => CheckEntity(report, uid, depth));

            var xform = Transform(child);
            if (!xform.Anchored)
                continue;

            var indices = _map.TileIndicesFor(grid, grid.Comp, xform.Coordinates);
            if (!_map.TryGetTileRef(grid, grid.Comp, indices, out var tile) || tile.Tile.IsEmpty)
            {
                Add(report, WFMappingIssueCatalog.MapAnchoredNoTile, child, xform.Coordinates, depth,
                    ("name", NameOf(child)));
            }

            if (KindOf(child) is not { } kind)
                continue;

            var key = (indices, kind);
            kinds[key] = kinds.TryGetValue(key, out var entry) ? (entry.First, entry.Count + 1) : (child, 1);
        }

        foreach (var ((indices, _), (first, count)) in kinds)
        {
            if (count < 2)
                continue;

            var center = new EntityCoordinates(grid, indices.X + 0.5f, indices.Y + 0.5f);
            Add(report, WFMappingIssueCatalog.MapDoubled, first, center, depth,
                ("name", NameOf(first)), ("count", count));
        }
    }

    /// <summary>What makes two anchored entities doubles: the same prototype, or cables of one voltage.</summary>
    private string? KindOf(EntityUid uid)
    {
        if (TryComp<CableComponent>(uid, out var cable))
            return $"cable:{cable.CableType}";

        return MetaData(uid).EntityPrototype?.ID;
    }

    /// <summary>The per-entity rules: a prototype the map file drops, and a denied component.</summary>
    private void CheckEntity(Report report, EntityUid uid, int depth)
    {
        report.Entities++;
        var coords = Transform(uid).Coordinates;
        if (MetaData(uid).EntityPrototype is { } prototype && WFMappingSpawnerIndex.WontSave(prototype))
        {
            var spawner = _mapping.SpawnerIndex.TryGetSpawner(prototype.ID, out var spawnerId) &&
                          _prototypes.TryIndex<EntityPrototype>(spawnerId, out var spawnerProto)
                ? string.IsNullOrEmpty(spawnerProto.Name) ? spawnerProto.ID : spawnerProto.Name
                : "none";
            Add(report, WFMappingIssueCatalog.MapUnsaved, uid, coords, depth,
                ("name", NameOf(uid)), ("spawner", spawner));
        }

        if (_mapping.HasDeniedComponent(uid))
            Add(report, WFMappingIssueCatalog.MapDenied, uid, coords, depth, ("name", NameOf(uid)));
    }

    /// <summary>Visits an entity and everything parented to it, containers included, observers left out.</summary>
    private void Walk(EntityUid root, Action<EntityUid> visit)
    {
        var stack = new Stack<EntityUid>();
        stack.Push(root);
        while (stack.TryPop(out var uid))
        {
            if (HasComp<WFMappingObserverComponent>(uid))
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
