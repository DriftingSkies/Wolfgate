using Content.Server.Parallax;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Vehicles.Falling;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Maths;

namespace Content.Server._WF.Vehicles.Falling;

/// <summary>The server's part of vehicle falling: it can tell planet ground nobody has loaded from a hole.</summary>
public sealed partial class VehicleFallSystem : SharedVehicleFallSystem
{
    [Dependency] private BiomeSystem _biome = default!;

    /// <inheritdoc/>
    // The same reading as CE's own fall: an empty tile of a planet layer that is neither pinned nor in a loaded chunk is
    // ground whose chunk is not loaded, and a body over it stands.
    protected override bool IsUnloadedGround(EntityUid grid, Vector2i tile)
    {
        if (!HasComp<WFPlanetLayerComponent>(grid) || !TryComp<BiomeComponent>(grid, out var biome))
            return false;

        Entity<BiomeComponent> ground = (grid, biome);
        return !_biome.WfIsPinned(ground, tile) && !_biome.WfIsChunkLoaded(ground, tile);
    }
}
