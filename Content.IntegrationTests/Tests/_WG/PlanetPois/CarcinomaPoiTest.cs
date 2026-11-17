#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._WG.PlanetPois;
using Content.Server._WG.Planets;
using Content.Server._WG.Planets.Bounds;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.PlanetPois;
using Content.Shared._WG.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.PlanetPois;

/// <summary>Carcinoma is small and keeps its sites far from the edge, so it is where a site runs out of room first.</summary>
[TestFixture]
public sealed class CarcinomaPoiTest
{
    private const string Surface = "WGSurfaceCarcinoma";
    private const string Table = "WGPoisCarcinoma";
    private const int Radius = 192;

    /// <summary>Every camp the table asks for finds a spot, and the world gets its ruins.</summary>
    [Test]
    public async Task EveryCampAndARuinFit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var holds = new List<Task>();
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, Table);
            var surface = proto.Index<WGPlanetSurfacePrototype>(Surface);
            var network = server.System<WGPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, Surface, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entMan.GetComponent<WGPlanetNetworkComponent>(network!.Value).Layers);

            var starting = new WGPlanetPreloadStartingEvent(layers[0], Vector2.Zero, Radius, holds);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref starting);
        });

        try
        {
            for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
                await pair.RunTicksSync(10);

            Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True, "The sites and roads were not laid.");

            await server.WaitAssertion(() =>
            {
                var record = entMan.GetComponent<WGPlanetPoiComponent>(layers[0]);
                var table = proto.Index<WGPlanetPoiTablePrototype>(Table);
                var pending = record.PendingCamps.GroupBy(camp => camp.Camp).ToDictionary(group => group.Key, group => group.Count());
                TestContext.Out.WriteLine($"sites: {string.Join(", ", record.Sites.Select(s => s.Name))}");

                foreach (var entry in table.Camps)
                {
                    Assert.That(pending.GetValueOrDefault(entry.Camp), Is.GreaterThanOrEqualTo(entry.MinCount),
                        $"{entry.Camp} found no room on {Surface}.");
                }
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
