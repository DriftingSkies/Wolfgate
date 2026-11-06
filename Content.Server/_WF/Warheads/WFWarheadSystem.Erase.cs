using System.Numerics;
using Content.Server.Decals;
using Content.Shared._WF.Warheads;
using Content.Shared.Ghost;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.Warheads;

/// <summary>Ground zero: everything within a payload's erase radius is gone, floor included, before the blast runs.</summary>
public sealed partial class WFWarheadSystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private readonly HashSet<EntityUid> _erased = new();
    private List<Entity<MapGridComponent>> _eraseGrids = new();

    /// <summary>
    /// Deletes every physical thing within a radius of ground zero, and everything on a ship's grid there, then its
    /// decals and, off a planet's own ground, its tiles. Ghosts and fallout stay; the dead come back as ghosts.
    /// </summary>
    public void Erase(MapCoordinates at, float radius)
    {
        if (radius <= 0f || !_map.TryGetMap(at.MapId, out var mapUid))
            return;

        _erased.Clear();
        _lookup.GetEntitiesInRange(at.MapId, at.Position, radius, _erased, LookupFlags.Uncontained);
        foreach (var uid in _erased)
        {
            if (TerminatingOrDeleted(uid) || HasComp<MapGridComponent>(uid) || HasComp<MapComponent>(uid)
                || HasComp<GhostComponent>(uid) || HasComp<WFFalloutComponent>(uid))
                continue;

            // Markers and system entities lie in space and on planets with no body; ships' fittings go regardless.
            var gridUid = Transform(uid).GridUid;
            if (!HasComp<PhysicsComponent>(uid) && (gridUid == null || gridUid == mapUid))
                continue;

            QueueDel(uid);
        }

        _eraseGrids.Clear();
        var reach = new Vector2(radius);
        _map.FindGridsIntersecting(at.MapId, new Box2(at.Position - reach, at.Position + reach), ref _eraseGrids, approx: true);
        foreach (var grid in _eraseGrids)
        {
            var local = Vector2.Transform(at.Position, _transform.GetInvWorldMatrix(grid));
            foreach (var (id, _) in _decals.GetDecalsInRange(grid, local, radius))
            {
                _decals.RemoveDecal(grid, id);
            }

            // A planet keeps its ground; a crater of void would open onto nothing.
            if (grid.Owner == mapUid)
                continue;

            var tiles = new List<(Vector2i, Tile)>();
            foreach (var tile in _map.GetLocalTilesIntersecting(grid, grid.Comp, new Circle(local, radius)))
            {
                tiles.Add((tile.GridIndices, Tile.Empty));
            }

            if (tiles.Count > 0)
                _map.SetTiles(grid, grid.Comp, tiles);
        }
    }
}
