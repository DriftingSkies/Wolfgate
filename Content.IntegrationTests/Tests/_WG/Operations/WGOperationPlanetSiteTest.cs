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
using Content.Shared.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.Operations;

/// <summary>A planetside operation claims fresh ground clear of every site, loads its grid there and gives it back.</summary>
[TestFixture]
public sealed class WGOperationPlanetSiteTest
{
    private const string Surface = "WGSurfaceCarcinoma";
    private const string Site = "/Maps/_WG/PlanetPois/abandoned_bandit_hideout.yml";

    [Test]
    public async Task SiteClaimsFreshGround()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var holds = new List<Task>();
        const int radius = 192;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, radius);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WGPoisCarcinoma");
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            var surface = server.ResolveDependency<IPrototypeManager>().Index<WGPlanetSurfacePrototype>(Surface);
            var network = server.System<WGPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, Surface, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WGPlanetNetworkComponent>(network!.Value).Layers);
            var preload = new WGPlanetPreloadStartingEvent(layers[0], Vector2.Zero, radius, holds);
            entities.EventBus.RaiseEvent(EventSource.Local, ref preload);
        });
        try
        {
            for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
                await pair.RunTicksSync(10);
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pois = server.System<WGPlanetPoiSystem>();
                var ground = layers[0];
                var record = entities.GetComponent<WGPlanetPoiComponent>(ground);
                Assert.That(record.NetworkReady, Is.True, record.GenerationError);

                Assert.That(pois.OperationGrounds(new List<string> { Surface }), Does.Not.Contain(ground),
                    "An excluded world was offered for an operation.");
                Assert.That(pois.OperationGrounds(new List<string>()), Does.Contain(ground));

                var before = record.Sites.Select(site => site.Bounds).ToList();
                Assert.That(pois.TryReserveOperationSite(ground, 12f, "wg-operation-name-bandit-den", out var spot, out var index),
                    Is.True, "No room for an operation site.");
                var claimed = record.Sites[index];
                Assert.Multiple(() =>
                {
                    Assert.That(before.Any(bounds => bounds.Intersects(claimed.Bounds)), Is.False,
                        "The operation site took over a point of interest.");
                    Assert.That(claimed.Expeditions, Is.False, "Expeditions and contracts would claim the operation's site.");
                    Assert.That(entities.GetComponent<WGPoiSignalComponent>(claimed.Signal).Revealed, Is.True,
                        "The operation's orbit signal should be named from the start.");
                });

                Assert.That(pois.TryLoadOperationSite(ground, new ResPath(Site), spot, Vector2.Zero, out var grid), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(entities.GetComponent<TransformComponent>(grid!.Value).MapUid, Is.EqualTo(ground));
                    Assert.That(entities.GetComponent<PhysicsComponent>(grid.Value).BodyType, Is.EqualTo(BodyType.Static),
                        "The site must sit on the ground, not fly.");
                });

                pois.ReleaseOperationSite(ground, index);
                Assert.That(claimed.Name.Id, Is.EqualTo("wg-operation-site-left"));
                Assert.That(claimed.Cleared, Is.True);
                entities.DeleteEntity(grid!.Value);
            });
        }
        finally { await PlanetFixture.Teardown(pair, layers); }
        await pair.CleanReturnAsync();
    }
}
