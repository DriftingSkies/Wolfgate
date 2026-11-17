#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._WG.Caverns;
using Content.Server._WG.Planets;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WG.Caverns;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Caverns;

/// <summary>Shared cavern test scaffolding, pulled in with <c>using static</c>.</summary>
public static class CavernFixture
{
    /// <summary>Every surface with a cavern below it.</summary>
    public static readonly string[] Surfaces =
    {
        "WGSurfaceAsclepiu",
        "WGSurfaceFervidus",
        "WGSurfaceMerak",
        "WGSurfaceAerumna",
        "WGSurfaceThrascias",
        "WGSurfaceCarcinoma",
    };

    /// <summary>Section 4: each world's rock substrate T, the floor of its tunnels, and its chamber floor C.</summary>
    public static readonly Dictionary<string, (string Substrate, string Chamber)> Floors = new()
    {
        { "WGSurfaceAsclepiu", ("WGCavernFloorLimestone", "FloorPlanetDirt") },
        { "WGSurfaceFervidus", ("FloorBasalt", "WGCavernFloorAsh") },
        { "WGSurfaceMerak", ("WGCavernFloorSand", "WGCavernFloorSandstone") },
        { "WGSurfaceAerumna", ("WGCavernFloorChromite", "WGCavernFloorBedrock") },
        { "WGSurfaceThrascias", ("FloorSnowDug", "WGCavernFloorSnowdrift") },
        { "WGSurfaceCarcinoma", ("WGCavernFloorFlesh", "WGCavernFloorGut") },
    };

    /// <summary>Biome chunk edge in tiles; SharedBiomeSystem.ChunkSize is protected.</summary>
    public const int ChunkSize = 8;

    /// <summary>A built world: its network, its surface layers ground first, and the cavern below it.</summary>
    public sealed class World
    {
        /// <summary>The planet network entity.</summary>
        public EntityUid Network;

        /// <summary>The network's surface layers, ground first and orbit last.</summary>
        public List<EntityUid> Layers = new();

        /// <summary>The network's maps below ground, nearest first.</summary>
        public List<EntityUid> LowerLayers = new();

        /// <summary>The cavern map, or Invalid when the world was built with caverns off.</summary>
        public EntityUid Cavern;

        /// <summary>The ground map.</summary>
        public EntityUid Ground => Layers[0];
    }

    /// <summary>Turns planet networks and caverns on for this pair; TestPair reverts both when the pair is returned.</summary>
    public static async Task EnableCaverns(TestPair pair)
    {
        await pair.Server.WaitPost(() =>
        {
            pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true);
            pair.Server.CfgMan.SetCVar(PlanetCVars.TerrainLoadBudget, 0f);
            pair.Server.CfgMan.SetCVar(PlanetCVars.Bounds, false);
            pair.Server.CfgMan.SetCVar(PlanetCVars.Preload, false);
            pair.Server.CfgMan.SetCVar(CavernCVars.Caverns, true);
        });
    }

    /// <summary>Stops lazy mouth claims for this pair, for a test that needs only the gate; TestPair reverts it when the pair is returned.</summary>
    public static async Task DisableClaims(TestPair pair)
    {
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(CavernCVars.CavernClaims, false));
    }

    /// <summary>Builds an unowned stack of one surface at the origin; with caverns on, its gate is claimed as it builds.</summary>
    public static async Task<World> BuildWorld(TestPair pair, string surfaceId)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WGPlanetNetworkSystem>();
        var world = new World();

        await server.WaitPost(() =>
        {
            var surface = proto.Index<WGPlanetSurfacePrototype>(surfaceId);
            var built = networks.BuildNetwork(surface, Vector2.Zero, surfaceId.Replace("WGSurface", string.Empty), null);

            Assert.That(built, Is.Not.Null, $"The {surfaceId} network failed to build.");

            var comp = entMan.GetComponent<WGPlanetNetworkComponent>(built!.Value);
            world.Network = built.Value;
            world.Layers = new List<EntityUid>(comp.Layers);
            world.LowerLayers = new List<EntityUid>(comp.LowerLayers);
            world.Cavern = comp.LowerLayers.FirstOrDefault();
        });

        await server.WaitRunTicks(1);
        return world;
    }

    /// <summary>Deletes the world's network, its cavern included.</summary>
    public static Task Teardown(TestPair pair, World world)
    {
        return PlanetFixture.Teardown(pair, world.Layers);
    }

    /// <summary>Whether an entity is the one the mouth lays on its landing tiles, the only thing besides the stairs a pad may hold.</summary>
    public static bool IsLandingEntity(IEntityManager entMan, EntityUid uid, WGCavernMouthSpec spec)
    {
        return spec.LandingEntity is { } landing && entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == landing.Id;
    }

    /// <summary>The map id of a layer, for the spawners that want one.</summary>
    public static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var mapId = MapId.Nullspace;

        await pair.Server.WaitPost(() => mapId = pair.Server.EntMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }

    /// <summary>Origins of every biome chunk overlapping a tile rectangle, corners included.</summary>
    public static IEnumerable<Vector2i> ChunkOrigins(Vector2i from, Vector2i to)
    {
        var min = SharedMapSystem.GetChunkIndices(from, ChunkSize);
        var max = SharedMapSystem.GetChunkIndices(to, ChunkSize);

        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        {
            yield return new Vector2i(x, y) * ChunkSize;
        }
    }

    /// <summary>Loads every biome chunk overlapping a tile rectangle, as a viewer standing there would.</summary>
    public static async Task LoadChunks(TestPair pair, EntityUid map, Vector2i from, Vector2i to)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();

        await server.WaitPost(() =>
        {
            var biome = (map, entMan.GetComponent<BiomeComponent>(map), entMan.GetComponent<MapGridComponent>(map));

            foreach (var origin in ChunkOrigins(from, to))
            {
                biomes.WgLoadChunk(biome, origin);
            }
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>Unloads every biome chunk overlapping a tile rectangle, as the loader does once nobody is near.</summary>
    public static async Task UnloadChunks(TestPair pair, EntityUid map, Vector2i from, Vector2i to)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();

        await server.WaitPost(() =>
        {
            var biome = (map, entMan.GetComponent<BiomeComponent>(map), entMan.GetComponent<MapGridComponent>(map));

            foreach (var origin in ChunkOrigins(from, to))
            {
                biomes.WgUnloadChunk(biome, origin);
            }
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>How many non-empty tiles a map has in a tile rectangle, corners included.</summary>
    public static int SolidTiles(IEntityManager entMan, SharedMapSystem maps, EntityUid map, Vector2i from, Vector2i to)
    {
        var grid = entMan.GetComponent<MapGridComponent>(map);
        var count = 0;

        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            if (maps.TryGetTileRef(map, grid, new Vector2i(x, y), out var tile) && !tile.Tile.IsEmpty)
                count++;
        }

        return count;
    }

    /// <summary>The world's gate mouth; fails the test when it has none.</summary>
    public static async Task<WGCavernMouth> Gate(TestPair pair, World world)
    {
        var server = pair.Server;
        var mouths = server.System<WGCavernMouthSystem>();
        WGCavernMouth? gate = null;

        await server.WaitPost(() =>
        {
            if (server.EntMan.TryGetComponent(world.Ground, out WGCavernGroundComponent? ground))
                gate = mouths.GetGate((world.Ground, ground));
        });

        Assert.That(gate, Is.Not.Null, $"{server.EntMan.ToPrettyString(world.Ground)} has no gate mouth.");
        return gate!;
    }

    /// <summary>The lip tile at the top of a mouth's stairs, the same index on the ground and in the cavern.</summary>
    public static Vector2i ClimbTile(WGCavernMouth mouth)
    {
        return mouth.ClimbTile;
    }

    /// <summary>The hole tile, the same index on the ground and in the cavern, that a mouth's stairs stand under.</summary>
    public static Vector2i StairsTile(WGCavernMouth mouth, WGCavernMouthSpec spec)
    {
        return mouth.ClimbTile - spec.StairsSide.ToIntVec();
    }

    /// <summary>
    /// The hole tile the fall tests drop through: the anchor, the one nearest the hole's centre, or, where the stairs
    /// stand under the anchor, the hole tile farthest from them.
    /// </summary>
    public static Vector2i DropTile(WGCavernMouth mouth, WGCavernMouthSpec spec)
    {
        var stairs = StairsTile(mouth, spec);

        return mouth.Origin != stairs
            ? mouth.Origin
            : mouth.Hole.OrderByDescending(tile => (tile - stairs).LengthSquared).ThenBy(tile => tile.X).ThenBy(tile => tile.Y).First();
    }

    /// <summary>The cavern tile a faller through <see cref="DropTile"/> lands on.</summary>
    public static Vector2i LandingIndex(WGCavernMouth mouth, WGCavernMouthSpec spec)
    {
        return DropTile(mouth, spec);
    }

    /// <summary>The centre of a tile in map-local coordinates.</summary>
    public static Vector2 TileCentre(Vector2i tile)
    {
        return new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
    }

    /// <summary>The cavern prototype under a surface.</summary>
    public static WGCavernPrototype CavernOf(TestPair pair, string surfaceId)
    {
        var caverns = pair.Server.System<WGCavernSystem>();

        Assert.That(caverns.TryGetCavern(surfaceId, out var cavern), Is.True, $"{surfaceId} has no wgCavern.");
        return cavern!;
    }

    /// <summary>Whether a map is the cavern or a transit gap that opens onto it.</summary>
    public static bool TouchesCavern(IEntityManager entMan, EntityUid? map, EntityUid cavern)
    {
        if (map == cavern)
            return true;

        return entMan.TryGetComponent(map, out CEZTransitMapComponent? transit)
               && (transit.LowerMap == cavern || transit.UpperMap == cavern);
    }

    /// <summary>Every transit gap that opens onto the cavern.</summary>
    public static List<EntityUid> TransitsTouchingCavern(IEntityManager entMan, EntityUid cavern)
    {
        var found = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<CEZTransitMapComponent>();

        while (query.MoveNext(out var uid, out var transit))
        {
            if (transit.LowerMap == cavern || transit.UpperMap == cavern)
                found.Add(uid);
        }

        return found;
    }
}
