using Content.Server._WF.Outposts.Components;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Outposts;

/// <summary>
/// The ring of pathfinding portals that joins an outpost's navmesh to the ground's, so NPCs walk on and off it and
/// raiders can path to its walls (spike F0.1).
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private PathfindingSystem _pathfinding = default!;

    private static readonly Vector2i[] Sides = { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right };

    /// <summary>Joins every edge tile of the outpost to the ground tile outside it, replacing any ring it had.</summary>
    public void RebuildPortals(Entity<WFOutpostComponent, MapGridComponent> outpost)
    {
        RemovePortals(outpost.Comp1);
        if (TerminatingOrDeleted(outpost.Comp1.Ground))
            return;

        var tiles = _maps.GetAllTilesEnumerator(outpost, outpost.Comp2);
        while (tiles.MoveNext(out var tile))
        {
            var index = tile.Value.GridIndices;
            foreach (var side in Sides)
            {
                var outside = index + side;
                if (_maps.TryGetTileRef(outpost, outpost.Comp2, outside, out var neighbour) && !neighbour.Tile.IsEmpty)
                    continue;

                var onOutpost = new EntityCoordinates(outpost, index + new System.Numerics.Vector2(0.5f));
                var outsideWorld = _transform.ToMapCoordinates(new EntityCoordinates(outpost, outside + new System.Numerics.Vector2(0.5f)));
                var onGround = _transform.ToCoordinates(outpost.Comp1.Ground, outsideWorld);
                if (_pathfinding.TryCreatePortal(onGround, onOutpost, out var handle))
                    outpost.Comp1.Portals.Add(handle);
            }
        }
    }

    private void RemovePortals(WFOutpostComponent outpost)
    {
        foreach (var handle in outpost.Portals)
        {
            _pathfinding.RemovePortal(handle);
        }

        outpost.Portals.Clear();
    }
}
