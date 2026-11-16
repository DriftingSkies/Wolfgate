#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Operations;

/// <summary>A planetside operation claims fresh ground clear of every site, loads its grid there and gives it back.</summary>
[TestFixture]
public sealed class WFOperationPlanetSiteTest
{
    private const string Surface = "WFSurfaceCarcinoma";
    private const string Site = "/Maps/_WF/PlanetPois/abandoned_bandit_hideout.yml";

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
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoisCarcinoma");
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            var surface = server.ResolveDependency<IPrototypeManager>().Index<WFPlanetSurfacePrototype>(Surface);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, Surface, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var preload = new WFPlanetPreloadStartingEvent(layers[0], Vector2.Zero, radius, holds);
            entities.EventBus.RaiseEvent(EventSource.Local, ref preload);
        });
        try
        {
            for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
                await pair.RunTicksSync(10);
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var pois = server.System<WFPlanetPoiSystem>();
                var ground = layers[0];
                var record = entities.GetComponent<WFPlanetPoiComponent>(ground);
                Assert.That(record.NetworkReady, Is.True, record.GenerationError);

                Assert.That(pois.OperationGrounds(new List<string> { Surface }), Does.Not.Contain(ground),
                    "An excluded world was offered for an operation.");
                Assert.That(pois.OperationGrounds(new List<string>()), Does.Contain(ground));

                var before = record.Sites.Select(site => site.Bounds).ToList();
                Assert.That(pois.TryReserveOperationSite(ground, 12f, "wf-operation-name-bandit-den", out var spot, out var index),
                    Is.True, "No room for an operation site.");
                var claimed = record.Sites[index];
                Assert.Multiple(() =>
                {
                    Assert.That(before.Any(bounds => bounds.Intersects(claimed.Bounds)), Is.False,
                        "The operation site took over a point of interest.");
                    Assert.That(claimed.Expeditions, Is.False, "Expeditions and contracts would claim the operation's site.");
                    Assert.That(entities.GetComponent<WFPoiSignalComponent>(claimed.Signal).Revealed, Is.True,
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
                Assert.That(claimed.Name.Id, Is.EqualTo("wf-operation-site-left"));
                Assert.That(claimed.Cleared, Is.True);
                entities.DeleteEntity(grid!.Value);
            });
        }
        finally { await PlanetFixture.Teardown(pair, layers); }
        await pair.CleanReturnAsync();
    }
}
