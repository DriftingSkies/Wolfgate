#nullable enable
#pragma warning disable RA0002 // The test reads the colony's people and the seat's colonists off their components.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Colonies;
using Content.Server._WF.PlanetCargoHubs;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.PlanetCargoHubs;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Colonies;

/// <summary>
/// A world built at Colony tier or past gets its seat among its sites: the depot stands for the colony with no outpost
/// on the world, its exchange trades on the colony's shelf, its bunks house people and some of them live there. A
/// chartered world's hall has the charter office.
/// </summary>
[TestFixture]
[TestOf(typeof(WFColonySystem))]
public sealed class ColonySeatTest
{
    private const string Surface = "WFSurfaceMerak";
    private const string Holder = "c900090";
    private const int Radius = 320;

    [TestCase(600f, "WFColonySeatDepot", false)]
    [TestCase(2500f, "WFColonySeatHall", true)]
    public async Task DevelopedWorldIsBuiltWithItsSeat(float points, string seatId, bool office)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var colonies = server.System<WFColonySystem>();
        var hubs = server.System<WFPlanetCargoHubSystem>();
        var market = server.System<WFTradeMarketSystem>();
        await PlanetFixture.EnableFeature(pair);
        await WaitFor(pair, () => colonies.Loaded);

        var layers = new List<EntityUid>();
        var holds = new List<Task>();
        var work = new List<Task>();
        EntityUid sectorMap = default;
        EntityUid body = default;
        EntityUid network = default;

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
                server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
                server.CfgMan.SetCVar(PlanetCargoHubCVars.Chance, 0f);
                server.CfgMan.SetCVar(ColonyCVars.Seats, true);
                hubs.ResetPlan();
                colonies.AddPoints(Surface, Holder, "Founders", points);

                sectorMap = server.System<SharedMapSystem>().CreateMap(out _);
                body = entMan.SpawnEntity(null, new EntityCoordinates(sectorMap, new Vector2(4000f, 0f)));
                server.System<MetaDataSystem>().SetEntityName(body, "Merak");
                entMan.AddComponent<WFSectorPlanetComponent>(body).Surface = Surface;

                var surface = server.ProtoMan.Index<WFPlanetSurfacePrototype>(Surface);
                var built = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Merak", body);
                Assert.That(built, Is.Not.Null);
                network = built!.Value;
                layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(network).Layers);
                var preload = new WFPlanetPreloadStartingEvent(layers[0], Vector2.Zero, Radius, holds);
                entMan.EventBus.RaiseEvent(EventSource.Local, ref preload);
            });

            for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
            {
                await pair.RunTicksSync(10);
            }

            var ground = layers[0];
            await server.WaitPost(() =>
            {
                Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True, "The world's sites failed to place.");
                var preloaded = new WFPlanetPreloadedEvent(ground, work);
                entMan.EventBus.RaiseEvent(EventSource.Local, ref preloaded);
            });

            for (var attempt = 0; attempt < 300 && work.Any(task => !task.IsCompleted); attempt++)
            {
                await pair.RunTicksSync(10);
            }

            await server.WaitAssertion(() =>
            {
                var proto = server.ProtoMan.Index<WFColonySeatPrototype>(seatId);
                var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
                Assert.That(record.GenerationError, Is.Null);
                var site = record.Sites.Single(placed => placed.Name == proto.Site.Name);
                Assert.That(site.Sockets, Is.Not.Empty, "The seat is off the roads.");
                Assert.That(entMan.GetComponent<WFPoiSignalComponent>(site.Signal).Revealed, Is.True, "The seat doesn't show from orbit.");

                Assert.That(colonies.TryGetSeat(network, out var seat, out var found), Is.True, "The world has no seat.");
                Assert.That(found!.ID, Is.EqualTo(seatId));
                Assert.That(colonies.HasDepot(network), Is.True);
                Assert.That(colonies.HasOffice(network), Is.EqualTo(office), "The charter office comes with the hall.");

                // The depot stands for the colony with no outpost on the world.
                colonies.Tick(1f);
                Assert.That(colonies.TryGetColony(Surface, out var colony), Is.True, "A depot should keep the colony standing.");
                Assert.That(colony.Owner, Is.EqualTo(body));
                Assert.That(colony.Comp.Housing, Is.EqualTo(proto.Housing), "The seat's bunks should house the colony's people.");

                // Its exchange trades on the colony's shelf.
                var machines = entMan.EntityQuery<WFColonyDepotMachineComponent, TransformComponent>()
                    .Where(machine => machine.Item2.GridUid == ground)
                    .Select(machine => machine.Item1.Owner)
                    .ToList();
                Assert.That(machines, Has.Count.EqualTo(2), "The depot has a vendor and a chute.");
                foreach (var machine in machines)
                    Assert.That(market.MarketOf(machine), Is.EqualTo(body), "The depot's exchange should trade on the colony's shelf.");

                // Some of its people live there, and one lost is a loss to the colony.
                Assert.That(seat.Comp.Colonists, Has.Count.EqualTo(1), "A colonist should move in.");
                var colonist = seat.Comp.Colonists[0];
                Assert.That(seat.Comp.Bounds.Contains(entMan.GetComponent<TransformComponent>(colonist).Coordinates.Position.Floored()), Is.True,
                    "The colonist should live at the seat.");
                var people = colony.Comp.People;
                entMan.DeleteEntity(colonist);
                colonies.Tick(1f);
                Assert.That(seat.Comp.Colonists, Does.Not.Contain(colonist));
                Assert.That(colony.Comp.People, Is.LessThan(people - 5f), "A colonist lost should cost the colony people.");
            });
        }
        finally
        {
            await server.WaitIdleAsync();
            await server.WaitPost(() => hubs.ResetPlan());
            if (layers.Count > 0)
                await PlanetFixture.Teardown(pair, layers);
            await server.WaitPost(() =>
            {
                if (sectorMap.IsValid())
                    entMan.DeleteEntity(sectorMap);
                colonies.ClearWorldForTest(Surface);
            });
            await Run(pair, () => colonies.Flush());
        }

        await pair.CleanReturnAsync();
    }

    private static async Task Run(TestPair pair, System.Func<Task> start)
    {
        Task? task = null;
        await pair.Server.WaitPost(() => task = start());
        for (var i = 0; i < 600 && !task!.IsCompleted; i++)
        {
            await pair.RunTicksSync(1);
            await Task.Delay(5);
        }

        Assert.That(task!.IsCompleted, Is.True, "The task didn't finish.");
        await task;
    }

    private static async Task WaitFor(TestPair pair, System.Func<bool> condition)
    {
        var done = false;
        for (var i = 0; i < 600 && !done; i++)
        {
            await pair.Server.WaitPost(() => done = condition());
            if (!done)
            {
                await pair.RunTicksSync(1);
                await Task.Delay(10);
            }
        }

        Assert.That(done, Is.True, "Timed out waiting.");
    }
}
