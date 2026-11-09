#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Contracts;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Contracts;
using Content.Shared._WF.Planets;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Contracts;

/// <summary>
/// Planetside contracts on a real world: a site is claimed and armed with the world's wildlife, cleared and released,
/// kits unfold on it, a chain's next link keeps the same site, and Carcinoma never gets a contract.
/// </summary>
[TestFixture]
[TestOf(typeof(WFContractSystem))]
public sealed class ContractPlanetTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wfPlanetPoiTable
  id: WFPoisContractTest
  minCount: 1
  maxCount: 1
  spacing: 32
  edgeMargin: 56
  centreClear: 24
  footprint: 24
  entries:
  - dungeon: WFPoiAsclepiuResearch
    name: wf-poi-asclepiu-research
";

    [Test]
    public async Task PlanetSitesRunAndRelease()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var contracts = entMan.System<WFContractSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var maps = entMan.System<SharedMapSystem>();

        await PlanetFixture.EnableFeature(pair);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, 128);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoisContractTest");
            server.CfgMan.SetCVar(PlanetCVars.PlanetExpeditions, false);
            // The site's wildlife would fight the test character's corpse-to-be; this is about placement, not combat.
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });

        var map = await pair.CreateTestMap();
        var layers = new List<EntityUid>();
        EntityUid network = default, central = default, board = default, mob = default;
        WFContractHolder holder = default;

        await server.WaitPost(() =>
        {
            var built = networks.BuildNetwork(proto.Index<WFPlanetSurfacePrototype>(PlanetFixture.SurfaceProto), new Vector2(20000f, 0f), "Asclepiu", null);
            Assert.That(built, Is.Not.Null, "The test world failed to build.");
            network = built!.Value;
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(network).Layers);

            central = ContractFlowTest.Station(server, map, Vector2.Zero, "Frontier");
            // Every planetside contractor's home, spread out so none crowds another.
            var homes = new[] { "AnomalousLab", "HeliosFortress", "Hokkaido", "Sevastopol", "TSFMCHalcyon", "TSFMCIndustry", "USSPCamelot", "WFAsfLantern" };
            for (var i = 0; i < homes.Length; i++)
            {
                var angle = i * System.MathF.Tau / homes.Length;
                ContractFlowTest.Station(server, map, new Vector2(System.MathF.Cos(angle), System.MathF.Sin(angle)) * 6000f, homes[i]);
            }
            board = entMan.SpawnEntity("WFContractBoard", new EntityCoordinates(ContractFlowTest.Largest(server, central), 4.5f, 4.5f));
            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ContractFlowTest.Largest(server, central), 2.5f, 2.5f));
            var minds = entMan.System<SharedMindSystem>();
            var mind = minds.CreateMind(pair.Player!.UserId, "Test Settler");
            minds.TransferTo(mind.Owner, mob);
            holder = contracts.HolderOf(mob)!.Value;
        });

        var ground = layers[0];
        try
        {
            for (var i = 0; i < 600 && !entMan.HasComponent<WFPlanetPreloadedComponent>(ground); i++)
            {
                await pair.RunTicksSync(10);
            }

            Assert.That(entMan.HasComponent<WFPlanetPreloadedComponent>(ground), Is.True, "The ground never finished preloading.");
            Assert.That(entMan.GetComponent<WFPlanetPoiComponent>(ground).NetworkReady, Is.True, "The world's sites were never placed.");
            WFPlanetPoiSite site = default!;

            // Clearing: dens and natives inside the site, the signal named; clearing completes it and gives the site back.
            WFContract dens = default!;
            await server.WaitAssertion(() =>
            {
                site = entMan.GetComponent<WFPlanetPoiComponent>(ground).Sites.Single();
                dens = Take(contracts, mob, board, "WFContractMarshalsPlanetDens");
                Assert.That(dens.PlanetWorld, Is.EqualTo(PlanetFixture.SurfaceProto));
                Assert.That(site.ContractClaim, Is.EqualTo(dens.Home), "The site was not claimed.");
                Assert.That(entMan.GetComponent<WFPoiSignalComponent>(site.Signal).Revealed, Is.True, "The site's signal was not named.");
                Assert.That(dens.Targets, Has.Count.InRange(2, 3));
                Assert.That(dens.Spawned, Has.Count.InRange(4, 6), "The site was not armed with the world's wildlife.");
                var fauna = proto.EnumeratePrototypes<WFContractWorldFaunaPrototype>().Single(f => f.World == PlanetFixture.SurfaceProto);
                foreach (var native in dens.Spawned)
                {
                    var id = entMan.GetComponent<MetaDataComponent>(native).EntityPrototype!.ID;
                    Assert.That(fauna.Light.Concat(fauna.Heavy).Concat(fauna.Boss).Any(m => m.Id == id), Is.True, $"{id} is not Asclepiu wildlife.");
                }

                foreach (var placed in dens.Targets.Concat(dens.Spawned))
                {
                    Assert.That(InSite(entMan, placed, ground, site), Is.True, "Something was placed outside the site.");
                }

                Assert.That(contracts.View(dens).Location, Does.Contain(entMan.GetComponent<MetaDataComponent>(ground).EntityName));
                foreach (var den in dens.Targets)
                {
                    entMan.QueueDeleteEntity(den);
                }
            });
            await pair.RunTicksSync(150);
            await server.WaitAssertion(() =>
            {
                Assert.That(dens.Status, Is.EqualTo(WFContractStatus.Completed), "Collapsing every den did not complete it.");
                Assert.That(site.ContractClaim, Is.Null, "The site was not given back.");
                Assert.That(dens.Spawned, Is.Empty);
            });

            // Construction, as a chain's first link: kits unfold on the site and the next link keeps the same site.
            WFContract claim = default!;
            await server.WaitAssertion(() =>
            {
                claim = Take(contracts, mob, board, "WFContractOutpostClaim", crewCheck: false);
                Assert.That(claim.Items, Has.Count.EqualTo(2));
                var middle = new Vector2((site.Bounds.Left + site.Bounds.Right) / 2f, (site.Bounds.Bottom + site.Bounds.Top) / 2f);
                var offset = 0f;
                foreach (var kit in claim.Items)
                {
                    xforms.SetCoordinates(kit, new EntityCoordinates(ground, middle + new Vector2(offset, 0f)));
                    offset += 2f;
                }
            });
            await pair.RunTicksSync(150);
            WFContract next = default!;
            await server.WaitAssertion(() =>
            {
                Assert.That(claim.Status, Is.EqualTo(WFContractStatus.Completed), "Kits set down on the site did not complete it.");
                var beacons = 0;
                var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out _, out var meta, out var xform))
                {
                    if (meta.EntityPrototype?.ID == "WFContractOutpostBeacon" && xform.MapUid == ground && xform.Anchored)
                        beacons++;
                }

                Assert.That(beacons, Is.EqualTo(2), "The kits did not unfold into anchored beacons on the site.");
                next = contracts.All.Single(c => c.Prototype == "WFContractOutpostClearance");
                Assert.That(next.ReservedFor, Is.EqualTo(holder.User));
                Assert.That(next.PreferredSite, Is.EqualTo(0), "The next link was not given the same site.");
                Assert.That(site.ContractClaim, Is.EqualTo(next.Home), "The site was not held for the next link.");
                next.Deposit = 0;
                Assert.That(contracts.ForceAccept(mob, next.Id, board), Is.True);
                Assert.That(next.PlanetSite, Is.EqualTo(0), "The next link did not build on the same site.");
                Assert.That(contracts.TryAbandon(holder, next.Id), Is.True);
                Assert.That(site.ContractClaim, Is.Null, "Abandoning the link did not give the site back.");
            });

            // Every planetside template in the game starts on the site with its objective inside it, and gives the site back.
            await server.WaitAssertion(() =>
            {
                var planetside = proto.EnumeratePrototypes<WFContractPrototype>().Where(p => p.Objective.Planet != null).ToList();
                Assert.That(planetside, Has.Count.GreaterThan(10));
                foreach (var template in planetside)
                {
                    var contract = Take(contracts, mob, board, template.ID, crewCheck: false);
                    Assert.That(contract.PlanetSite, Is.EqualTo(0), $"{template.ID} did not claim the site.");
                    Assert.That(site.ContractClaim, Is.EqualTo(contract.Home), $"{template.ID} did not mark the site claimed.");
                    var placed = contract.Targets.Concat(contract.Anomalies).Concat(contract.Spawned).ToList();
                    if (template.Objective is WFContractRecovery)
                        placed.AddRange(contract.Items);
                    Assert.That(placed, Is.Not.Empty, $"{template.ID} put nothing on the site.");
                    foreach (var uid in placed)
                    {
                        Assert.That(InSite(entMan, uid, ground, site), Is.True, $"{template.ID} placed something outside the site.");
                    }

                    if (template.Objective is WFContractConstruct construct)
                    {
                        Assert.That(contract.Items, Has.Count.EqualTo(contract.Count), $"{template.ID} handed out the wrong number of kits.");
                        foreach (var kit in construct.Kits)
                        {
                            var handed = contract.Items.Count(i => entMan.GetComponent<MetaDataComponent>(i).EntityPrototype!.ID == kit.Entity.Id);
                            Assert.That(handed, Is.InRange(kit.Count.X, kit.Count.Y), $"{template.ID} handed out {handed} of {kit.Entity}.");
                        }
                    }

                    Assert.That(contracts.TryAbandon(holder, contract.Id), Is.True);
                    Assert.That(site.ContractClaim, Is.Null, $"{template.ID} kept the site after it was abandoned.");
                }
            });

            // A planetside download: standing by the terminal runs it and brings the world's wildlife in waves.
            WFContract archive = default!;
            var before = 0;
            await server.WaitAssertion(() =>
            {
                archive = Take(contracts, mob, board, "WFContractSevPlanetArchive", crewCheck: false);
                before = archive.Spawned.Count;
                xforms.SetCoordinates(mob, entMan.GetComponent<TransformComponent>(archive.Targets[0]).Coordinates.Offset(new Vector2(1f, 0f)));
            });
            await pair.RunTicksSync(150);
            await server.WaitAssertion(() =>
            {
                Assert.That(archive.Started && archive.Progress > System.TimeSpan.Zero, Is.True, "Standing by the terminal did not run the download.");
                Assert.That(archive.Spawned.Count, Is.GreaterThan(before), "No wave of the world's wildlife came.");
                foreach (var native in archive.Spawned)
                {
                    Assert.That(InSite(entMan, native, ground, site), Is.True, "A wave landed outside the site.");
                }

                Assert.That(contracts.TryAbandon(holder, archive.Id), Is.True);
                xforms.SetCoordinates(mob, new EntityCoordinates(ContractFlowTest.Largest(server, central), 2.5f, 2.5f));
            });

            // Carcinoma: the same world under Carcinoma's name gets no planetside offer at all.
            await server.WaitAssertion(() =>
            {
                var comp = entMan.GetComponent<WFPlanetNetworkComponent>(network);
                var surface = comp.Surface;
                comp.Surface = "WFSurfaceCarcinoma";
                try
                {
                    Assert.That(contracts.PostOffer("WFContractMarshalsPlanetDens"), Is.Null, "A contract was posted on Carcinoma.");
                }
                finally
                {
                    comp.Surface = surface;
                }
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static bool InSite(IEntityManager entMan, EntityUid uid, EntityUid ground, WFPlanetPoiSite site)
    {
        var xform = entMan.GetComponent<TransformComponent>(uid);
        if (xform.MapUid != ground)
            return false;

        var tile = new Vector2i((int) System.MathF.Floor(xform.LocalPosition.X), (int) System.MathF.Floor(xform.LocalPosition.Y));
        return site.Bounds.Contains(tile);
    }

    private static WFContract Take(WFContractSystem contracts, EntityUid mob, EntityUid board, string id, bool crewCheck = true)
    {
        var offer = contracts.PostOffer(id)!;
        Assert.That(offer, Is.Not.Null, $"{id} could not be posted.");
        offer.Deposit = 0;
        if (!crewCheck)
        {
            Assert.That(contracts.ForceAccept(mob, offer.Id, board), Is.True, $"{id} did not start.");
            return offer;
        }

        Assert.That(contracts.TryAccept(mob, offer.Id, board), Is.Null, $"{id} was not accepted.");
        return offer;
    }
}
