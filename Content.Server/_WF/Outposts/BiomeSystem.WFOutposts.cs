using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server.Parallax;

/// <summary>
/// Carving a planet's ground out from under an outpost, so its own floor is the only floor there: no ground navmesh
/// under its walls, no weather or wildlife on ground it covers. Carved tiles are marked modified so the biome never
/// grows them back, and come back as they were when the outpost goes.
/// </summary>
public sealed partial class BiomeSystem
{
    /// <summary>Whether an anchored entity on a ground tile was laid there by the biome: natural growth, not someone's build.</summary>
    public bool WfIsBiomeAnchored(EntityUid ground, EntityUid ent, Vector2i index)
    {
        var chunk = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;
        return TryComp<BiomeComponent>(ground, out var biome)
               && biome.LoadedEntities.TryGetValue(chunk, out var loaded)
               && loaded.ContainsKey(ent);
    }

    /// <summary>
    /// Removes the ground tiles at <paramref name="indices"/> and the natural growth anchored on them, noting each tile
    /// taken in <paramref name="carved"/>.
    /// </summary>
    public void WfCarve(Entity<MapGridComponent> ground, IEnumerable<Vector2i> indices, Dictionary<Vector2i, Tile> carved)
    {
        TryComp<BiomeComponent>(ground, out var biome);
        var empty = new List<(Vector2i, Tile)>();
        var growth = new List<EntityUid>();
        foreach (var index in indices)
        {
            if (biome != null)
            {
                var chunk = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;
                biome.ModifiedTiles.GetOrNew(chunk).Add(index);
                if (biome.LoadedEntities.TryGetValue(chunk, out var loaded))
                {
                    var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(ground, ground.Comp, index);
                    while (anchored.MoveNext(out var ent))
                    {
                        if (loaded.Remove(ent.Value))
                            growth.Add(ent.Value);
                    }
                }
            }

            if (!_mapSystem.TryGetTileRef(ground, ground.Comp, index, out var tile) || tile.Tile.IsEmpty)
            {
                // A chunk not loaded yet never lays a modified tile, so note the one it would have laid.
                if (biome != null && !biome.LoadedChunks.Contains(SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize)
                    && TryGetBiomeTile(index, biome.Layers, biome.Seed, ground, out var natural))
                    carved.TryAdd(index, natural.Value);
                continue;
            }

            carved.TryAdd(index, tile.Tile);
            empty.Add((index, Tile.Empty));
        }

        foreach (var ent in growth)
        {
            QueueDel(ent);
        }

        if (empty.Count > 0)
            _mapSystem.SetTiles(ground, ground.Comp, empty);
    }

    /// <summary>Puts carved ground back wherever it is still bare.</summary>
    public void WfRestoreCarved(Entity<MapGridComponent> ground, Dictionary<Vector2i, Tile> carved)
    {
        var restore = new List<(Vector2i, Tile)>();
        foreach (var (index, tile) in carved)
        {
            if (!_mapSystem.TryGetTileRef(ground, ground.Comp, index, out var now) || now.Tile.IsEmpty)
                restore.Add((index, tile));
        }

        if (restore.Count > 0)
            _mapSystem.SetTiles(ground, ground.Comp, restore);

        carved.Clear();
    }
}
