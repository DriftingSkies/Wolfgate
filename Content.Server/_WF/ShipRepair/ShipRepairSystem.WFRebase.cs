using System.Numerics;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.Map;

namespace Content.Server._Mono.ShipRepair;

public sealed partial class ShipRepairSystem
{
    /// <summary>
    /// Moves every tile and entity in a snapshot by <paramref name="shift"/> tiles, re-bucketing them into the chunks
    /// of their new indices. Entities get new ids in their new chunks.
    /// </summary>
    public void WfShiftSnapshot(ShipRepairDataComponent data, Vector2i shift, float tileSize)
    {
        if (shift == Vector2i.Zero)
            return;

        var size = data.ChunkSize;
        var old = new List<(Vector2i Index, ShipRepairChunk Chunk)>(data.Chunks.Count);
        foreach (var (index, chunk) in data.Chunks)
        {
            old.Add((index, chunk));
        }

        data.Chunks.Clear();

        foreach (var (chunkIndex, chunk) in old)
        {
            for (var i = 0; i < chunk.Tiles.Length; i++)
            {
                if (chunk.Tiles[i] == Tile.Empty.TypeId)
                    continue;

                var index = chunkIndex * size + new Vector2i(i % size, i / size) + shift;
                var relative = GetRelativeIndices(index, size);
                GetCreateChunk(data, index).Tiles[relative.X + relative.Y * size] = chunk.Tiles[i];
            }
        }

        var offset = (Vector2) shift * tileSize;
        foreach (var (_, chunk) in old)
        {
            foreach (var spec in chunk.Entities.Values)
            {
                spec.LocalPosition += offset;
                var tile = new Vector2i(
                    (int) MathF.Floor(spec.LocalPosition.X / tileSize),
                    (int) MathF.Floor(spec.LocalPosition.Y / tileSize));
                var target = GetCreateChunk(data, tile);
                target.Entities[target.NextUid++] = spec;
            }
        }
    }
}
