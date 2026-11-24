using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server.Parallax;

/// <summary>Wolfgate access to BiomeComponent's private chunk loader and pin set.</summary>
public sealed partial class BiomeSystem
{
    /// <summary>Tile accumulator for <see cref="WgUnloadChunk"/>; the private unloader writes into the caller's list.</summary>
    private readonly List<(Vector2i, Tile)> _wgUnloadBuffer = new();

    /// <summary>Pins tiles so the biome never regenerates or unloads them again.</summary>
    public void WgPinTiles(Entity<BiomeComponent> biome, IReadOnlyCollection<Vector2i> indices)
    {
        foreach (var index in indices)
        {
            var chunkOrigin = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;
            biome.Comp.ModifiedTiles.GetOrNew(chunkOrigin).Add(index);
        }
    }

    /// <summary>Lets the biome generate on tiles again.</summary>
    public void WgUnpinTiles(Entity<BiomeComponent> biome, IEnumerable<Vector2i> indices)
    {
        foreach (var index in indices)
        {
            var chunkOrigin = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;
            if (biome.Comp.ModifiedTiles.TryGetValue(chunkOrigin, out var modified))
                modified.Remove(index);
        }
    }

    /// <summary>Whether one tile index is pinned against biome regeneration.</summary>
    public bool WgIsPinned(Entity<BiomeComponent> biome, Vector2i index)
    {
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;

        return biome.Comp.ModifiedTiles.TryGetValue(chunkOrigin, out var modified) && modified.Contains(index);
    }

    /// <summary>
    /// Loads one biome chunk by hand and holds it: the planet unloader leaves it until <see cref="WgUnloadChunk"/>.
    /// Claims LoadedChunks first, as LoadChunk throws on a repeat. Not held, it goes by the unloader's rules.
    /// </summary>
    public bool WgLoadChunk(Entity<BiomeComponent, MapGridComponent> biome, Vector2i chunkOrigin, bool hold = true)
    {
        if (hold)
            _wgLayers.GetOrNew(biome.Owner).Held.Add(chunkOrigin);

        if (!biome.Comp1.LoadedChunks.Add(chunkOrigin))
        {
            // Loaded but stashed: its entities are laid again.
            WgRestoreChunk(biome.Comp1, biome.Owner, biome.Comp2, chunkOrigin);
            return false;
        }

        LoadChunk(biome.Comp1, biome.Owner, biome.Comp2, chunkOrigin, biome.Comp1.Seed);
        return true;
    }

    /// <summary>Loads and holds every chunk an area touches, so its terrain stands before anything is placed on it; returns them.</summary>
    public List<Vector2i> WgHoldArea(Entity<BiomeComponent, MapGridComponent> biome, Box2i area)
    {
        var held = new List<Vector2i>();
        var low = SharedMapSystem.GetChunkIndices(area.BottomLeft, ChunkSize);
        var high = SharedMapSystem.GetChunkIndices(area.TopRight - Vector2i.One, ChunkSize);
        for (var x = low.X; x <= high.X; x++)
        for (var y = low.Y; y <= high.Y; y++)
        {
            var origin = new Vector2i(x, y) * ChunkSize;
            WgLoadChunk(biome, origin);
            held.Add(origin);
        }

        return held;
    }

    /// <summary>Lets the unloader have chunks held by <see cref="WgHoldArea"/> again, without unloading them now.</summary>
    public void WgReleaseChunks(EntityUid layer, IEnumerable<Vector2i> chunks)
    {
        if (!_wgLayers.TryGetValue(layer, out var unload))
            return;

        foreach (var chunk in chunks)
        {
            unload.Held.Remove(chunk);
        }
    }

    /// <summary>
    /// Unloads one biome chunk by hand, emptying every unpinned tile and its decals, as the loader would: a planet
    /// layer's chunk goes by the planet unloader's rules.
    /// </summary>
    public bool WgUnloadChunk(Entity<BiomeComponent, MapGridComponent> biome, Vector2i chunkOrigin)
    {
        if (_wgLayers.TryGetValue(biome.Owner, out var layer))
            layer.Held.Remove(chunkOrigin);

        if (!biome.Comp1.LoadedChunks.Contains(chunkOrigin))
            return false;

        if (HasComp<Content.Shared._WG.Planets.WGPlanetLayerComponent>(biome.Owner)
            && _configManager.GetCVar(Content.Shared._WG.CCVar.PlanetCVars.TerrainUnload))
        {
            WgUnloadChunkPristine(biome.Comp1, biome.Owner, biome.Comp2, chunkOrigin);
            return true;
        }

        _wgUnloadBuffer.Clear();
        UnloadChunk(biome.Comp1, biome.Owner, biome.Comp2, chunkOrigin, biome.Comp1.Seed, _wgUnloadBuffer);
        return true;
    }
}
