#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>A ship docked at a station is in sanctuary from Reaver raids and intercepts, and one that berths mid-run is let go.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverSanctuaryTest : WFReaverTestBase
{
    private int Ticks(double seconds) => (int) Math.Ceiling(seconds * STiming.TickRate);

    /// <summary>Gives a ship a port and docks it to a station beside it.</summary>
    private (EntityUid Port, EntityUid StationPort) Berth(EntityUid ship, EntityUid station)
    {
        var transform = Server.System<SharedTransformSystem>();
        transform.SetWorldPosition(station, transform.GetWorldPosition(ship) + new Vector2(4f, 0f));
        var port = SEntMan.SpawnAtPosition("AirlockShuttle", new EntityCoordinates(ship, new Vector2(0.5f)));
        var stationPort = SEntMan.EntityQuery<DockingComponent, TransformComponent>()
            .Single(pair => pair.Item2.GridUid == station).Item1.Owner;
        Server.System<DockingSystem>().Dock((port, SEntMan.GetComponent<DockingComponent>(port)),
            (stationPort, SEntMan.GetComponent<DockingComponent>(stationPort)));
        return (port, stationPort);
    }

    private void Unberth(EntityUid port)
    {
        Server.System<DockingSystem>().Undock((port, SEntMan.GetComponent<DockingComponent>(port)));
    }

    private List<WFCrewObjective> Orders(EntityUid encounter, string key)
    {
        var group = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships[key].Group;
        return Server.System<WFCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == group).Objectives;
    }

    /// <summary>The campaign deals with no ship berthed at a station: it is left out of the crewed ships, and neither raided nor intercepted.</summary>
    [Test]
    public async Task ShipBerthedAtAStationIsNeitherRaidedNorIntercepted()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            FoundAt(Home);
            campaign.SetTier(3);
            var ship = SpawnGrid(CentreOf(Home.Neighbour(1)) + new Vector2(0f, 1500f), "Test Hauler");
            var station = SpawnStation(CentreOf(Home.Neighbour(1)) + new Vector2(0f, 1500f), "Test Depot");
            Board(ship);
            var port = Berth(ship, station).Port;
            var state = campaign.State()!;

            Assert.That(campaign.TryLaunchRaid(), Is.False, "No raid goes for a berthed ship.");
            Assert.That(campaign.TryIntercept(ship), Is.False, "No patrol is turned on it.");
            campaign.CheckIntercepts();
            state.InsideSince[ship] = STiming.CurTime - TimeSpan.FromMinutes(5);
            campaign.CheckIntercepts();
            Assert.That(state.InsideSince.ContainsKey(ship), Is.False, "Its time in held space is not counted.");
            Assert.That(campaign.Patrols(), Is.Empty, "Nobody is sent after it.");

            Unberth(port);
            Assert.That(campaign.TryIntercept(ship), Is.True, "Cast off, it is fair game.");
        });
    }

    /// <summary>A patrol closing on a ship gives it up, and flies its route on, when the ship berths at a station.</summary>
    [Test]
    public async Task InterceptEndsWhenTheTargetBerthsAtAStation()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            FoundAt(Home);
            campaign.SetTier(1);
            var ship = SpawnGrid(CentreOf(Home.Neighbour(1)), "Test Hauler");
            var station = SpawnStation(CentreOf(Home.Neighbour(1)), "Test Depot");
            Board(ship);
            Assert.That(campaign.TryIntercept(ship), Is.True);
            var patrol = campaign.Patrols().Single();
            Assert.That(patrol.Comp1.Intercepting, Is.EqualTo(ship));

            Berth(ship, station);
            campaign.CheckIntercepts();
            Assert.That(patrol.Comp1.Intercepting, Is.Null, "The patrol gives the ship up.");
            Assert.That(Orders(patrol.Owner, "lead").Select(order => order.Kind), Is.All.EqualTo(WFCrewObjectiveKind.GoTo),
                "It flies its route and home.");
        });
    }

    /// <summary>A raid on a ship that berths at a station is called off: a pack flies home, and hunters let their prey go.</summary>
    [Test]
    public async Task RaidIsCalledOffWhenTheTargetBerthsAtAStation()
    {
        await StartCampaign();
        EntityUid ship = default, station = default, raid = default;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            FoundAt(Home);
            ship = SpawnGrid(CentreOf(Home.Neighbour(1)) + new Vector2(0f, 1500f), "Test Hauler");
            station = SpawnStation(CentreOf(Home.Neighbour(1)) + new Vector2(0f, 1500f), "Test Depot");
            Board(ship);
            Assert.That(campaign.TryLaunchRaid(), Is.True);
            raid = Raids().Single();
            Assert.That(SEntMan.GetComponent<WFReaverRaidComponent>(raid).Target, Is.EqualTo(ship));
            Berth(ship, station);
        });

        await RunTicks(Ticks(3));
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFReaverRaidComponent>(raid);
            Assert.That(comp.CalledOff, Is.True, "The raid is called off.");
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(raid);
            if (encounter.Prototype.Id == Pack)
            {
                var orders = Orders(raid, "raider");
                Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo }), "The pack flies home.");
                Assert.That(Vector2.Distance(orders[0].Position, encounter.Origin.Position), Is.LessThan(1f));
            }
            else
            {
                Assert.That(encounter.Ships["boarder"].Prey, Is.Not.EqualTo(ship), "The boarders let the ship go.");
            }
        });
    }
}
