using Content.Server._WF.Outposts.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Parallax;

/// <summary>Lays foundation rock in place of the biome's own on a cavern's tiles under an outpost's claim.</summary>
// Laid and tracked as the chunk's own, so it unloads and comes back with the chunk like any rock.
public sealed partial class BiomeSystem
{
    public static readonly EntProtoId WfFoundationRock = "WFOutpostFoundationRock";

    /// <summary>Lays foundation rock on a tile under a claim; false where the loader lays the biome's own.</summary>
    private bool WfLayFoundation(EntityUid map, MapGridComponent grid, Vector2i tile, Dictionary<EntityUid, Vector2i> loaded)
    {
        if (!TryComp<WFOutpostFoundationComponent>(map, out var foundation) || !foundation.Covers(tile)
            || !_mapSystem.TryGetTileRef(map, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return false;

        var rock = Spawn(WfFoundationRock, _mapSystem.GridTileToLocal(map, grid, tile));
        if (_xformQuery.TryGetComponent(rock, out var xform) && !xform.Anchored)
            _transform.AnchorEntity((rock, xform), (map, grid), tile);

        loaded.Add(rock, tile);
        return true;
    }
}
