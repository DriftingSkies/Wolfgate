using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.DroneRings;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Prototypes;
using Content.Shared._WF.DroneRings;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._WF.DroneRings;

/// <summary>Checks where the drone rings fall for a set of space biomes, and that a generated map gets them.</summary>
public sealed class WFDroneRingTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFDroneRingTestLightSpawner

- type: entity
  id: WFDroneRingTestHeavySpawner

- type: wfDroneSources
  id: WFDroneRingTest
  spawners:
  - WFDroneRingTestLightSpawner
  - WFDroneRingTestHeavySpawner

- type: spaceBiome
  id: WFDroneRingTestEmpty
  priority: 1
  distanceRange: 0, 1000
  noiseRanges: {}

- type: spaceBiome
  id: WFDroneRingTestLight
  priority: 0
  distanceRange: 1000, 2000
  noiseRanges: {}
  chunkComponents:
  - type: NoiseDrivenDebrisSelector
    noiseChannel: Wreck
    debrisTable:
    - id: WFDroneRingTestLightSpawner

- type: spaceBiome
  id: WFDroneRingTestLightAgain
  priority: 0
  distanceRange: 2000, 3000
  noiseRanges: {}
  chunkComponents:
  - type: SimpleDebrisSelector
    debrisTable:
    - id: WFDroneRingTestLightSpawner

- type: spaceBiome
  id: WFDroneRingTestHeavy
  priority: -1
  noiseRanges: {}
  chunkComponents:
  - type: NoiseDrivenDebrisSelector
    noiseChannel: Wreck
    debrisTable:
    - id: WFDroneRingTestHeavySpawner

- type: spaceBiome
  id: WFDroneRingTestPatch
  priority: 10
  minX: 0
  maxX: 1
  minY: 0
  maxY: 1
  noiseRanges: {}
  chunkComponents:
  - type: NoiseDrivenDebrisSelector
    noiseChannel: Wreck
    debrisTable:
    - id: WFDroneRingTestHeavySpawner
";

    /// <summary>
    /// A ring falls where drones first appear and where the mix changes, including at the start of the unbounded
    /// outer biome. A band with the same drones as the last and a patch pinned to chunks draw none.
    /// </summary>
    [Test]
    public async Task RingsFollowBiomeSelection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var rings = server.ResolveDependency<IEntityManager>().System<WFDroneRingSystem>();

        await server.WaitAssertion(() =>
        {
            var radii = rings.Rings(new List<string>
            {
                "WFDroneRingTestHeavy",
                "WFDroneRingTestPatch",
                "WFDroneRingTestLightAgain",
                "WFDroneRingTestEmpty",
                "WFDroneRingTestLight",
            });
            Assert.That(radii, Is.EqualTo(new List<float> { 1000f, 3000f }));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The default world generation gives its map rings, each at the edge of one of its biomes.</summary>
    [Test]
    public async Task DefaultWorldgenMapGetsRings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var serialization = server.ResolveDependency<ISerializationManager>();
        var config = server.ResolveDependency<IConfigurationManager>().GetCVar(CCVars.WorldgenConfig);

        await server.WaitAssertion(() =>
        {
            var selection = protos.Index<WorldgenConfigPrototype>(config).Components.Values
                .Select(entry => entry.Component)
                .OfType<BiomeSelectionComponent>()
                .Single();
            var biomes = selection.Biomes;
            var edges = new HashSet<float>();
            foreach (var id in biomes)
            {
                if (protos.Index<BiomePrototype>(id).DistanceRange is not { } range)
                    continue;

                edges.Add(range.X);
                edges.Add(range.Y);
            }

            // Biome selection alone is inert: nothing generates without a world controller.
            entities.AddComponent(map.MapUid, serialization.CreateCopy(selection, notNullableOverride: true));
            var radii = entities.GetComponent<WFDroneRingsComponent>(map.MapUid).Radii;
            Assert.That(radii, Is.Not.Empty);
            Assert.That(radii, Is.Ordered.Ascending);
            Assert.That(radii, Is.Unique);
            Assert.That(radii, Is.SubsetOf(edges));
        });

        await pair.CleanReturnAsync();
    }
}
