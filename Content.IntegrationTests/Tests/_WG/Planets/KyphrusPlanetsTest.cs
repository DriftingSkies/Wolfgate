using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Planets;
using Content.Shared._WG.Planets;
using Robust.Shared.GameObjects;
using static Content.IntegrationTests.Tests._WG.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WG.Planets;

[TestFixture]
public sealed class KyphrusPlanetsTest
{
    [TestCase("PlanetFervidus", "Fervidus")]
    [TestCase("PlanetMerak", "Merak")]
    [TestCase("PlanetAerumna", "Aerumna")]
    [TestCase("PlanetThrascias", "Thrascias")]
    [TestCase("WGPlanetCarcinoma", "Carcinoma")]
    public async Task NewWorldBuilds(string planet, string name)
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var em = server.EntMan;
        var layers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var registry = server.System<WGPlanetRegistrySystem>();
            Assert.That(registry.TryGetSurface(planet, out var surface), Is.True,
                "The sector body must automatically resolve its surface.");
            Assert.That(surface!.BuildAtRoundStart, Is.True);
            Assert.That(surface.CloudLayer, Is.False, "A cloud deck would hide the surface from orbit.");
            var network = server.System<WGPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, name, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(em.GetComponent<WGPlanetNetworkComponent>(network!.Value).Layers);
            Assert.That(layers, Has.Count.EqualTo(5));
            var orbit = em.GetComponent<WGOrbitLayerComponent>(layers[^1]);
            Assert.That(orbit.RadarLayers, Is.Not.Empty);
            Assert.That(orbit.RadarSeed, Is.EqualTo(surface.Seed));
            var radar = server.System<WGPlanetRadarSystem>();
            for (var x = -100; x <= 100; x += 25)
            for (var y = -100; y <= 100; y += 25)
                Assert.That(radar.Sample(orbit, new Vector2(x, y)), Is.Not.Null,
                    "The natural surface must not have noise gaps.");
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
