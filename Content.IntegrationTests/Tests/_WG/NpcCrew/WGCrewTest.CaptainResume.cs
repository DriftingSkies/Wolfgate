#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Evasive departure keeps the saved course and freezes a timed objective until all-clear.</summary>
    [Test]
    public async Task CaptainEvasionContinuationPreservesTimedQueue()
    {
        var (deck, station, ownDock, _) = await CreateDockingPair(gravity: true);
        var target = await CreateDeck(new Vector2(300, 300), 7, gravity: true);
        EntityUid pilot = default, helm = default;
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            helm = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(3.5f)));
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var docking = Server.System<DockingSystem>();
            var configuration = docking.GetDockingConfig(deck, station);
            Assert.That(configuration, Is.Not.Null);
            Server.System<ShuttleSystem>().FTLDock((deck, SEntMan.GetComponent<TransformComponent>(deck)), configuration!);
            var crew = Server.System<WGCrewSystem>();
            var captain = crew.SpawnCrewman(WGCrewRoles.Captain,
                new EntityCoordinates(deck, new Vector2(1.5f)), "evading-departure")!.Value;
            pilot = crew.SpawnCrewman(WGCrewRoles.Pilot,
                new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)), "evading-departure")!.Value;
            SEntMan.GetComponent<HTNComponent>(captain).Enabled = false;
            SEntMan.GetComponent<HTNComponent>(pilot).Enabled = false;
        });
        await WaitUntil(() => Server.System<WGPilotDutySystem>().TryFindHelm(pilot, out var available) && available == helm,
            120, () => DescribePilot(pilot, helm));
        await Server.WaitAssertion(() =>
        {
            var docking = Server.System<DockingSystem>();
            Assert.That(Server.System<WGPilotDutySystem>().TryTakeHelm(pilot, helm), Is.True, DescribePilot(pilot, helm));
            var objectives = Server.System<WGCrewObjectiveSystem>();
            Assert.That(objectives.SetQueue(deck, "evading-departure", new()
            {
                new WGCrewObjective { Kind = WGCrewObjectiveKind.Follow, Target = SEntMan.GetNetEntity(target), Range = 160, Duration = 3 },
            }), Is.True);
            // Start the queued departure and interrupt it before its next physics update.
            objectives.Update(0.5f);
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(pilot);
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Undock));
            Assert.That(duty.ResumeOrder, Is.EqualTo(WGPilotOrder.Follow));
            Server.System<WGCrewAlertSystem>().ReportShipThreat(deck, "evading-departure", target);
            Assert.That(duty.ResumeOrder, Is.EqualTo(WGPilotOrder.Loiter));
            Assert.That(Server.System<WGCaptainSystem>().IsCourseSuspended(pilot), Is.True);
            // Complete physical separation; the next pilot update resumes its pending evasive order.
            docking.Undock((ownDock, SEntMan.GetComponent<DockingComponent>(ownDock)));
        });
        await WaitUntil(() => SEntMan.GetComponent<WGPilotDutyComponent>(pilot).Orders == WGPilotOrder.Loiter,
            120, () => DescribePilot(pilot, helm));
        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGCaptainSystem>().IsCourseSuspended(pilot), Is.True,
                "Automatic Undock-to-Loiter must not count as a replacement command.");
            var row = Server.System<WGCrewObjectiveSystem>().Snapshot().Single(item => item.Group == "evading-departure");
            Assert.That(row.Objectives, Has.Count.EqualTo(1), "The three-second task must not expire during five seconds of evasion.");
        });
        await WaitUntil(() => !Server.System<WGCaptainSystem>().IsCourseSuspended(pilot),
            4000, () => "Captain did not restore its course after the hull alert expired.");
        await Server.WaitAssertion(() =>
        {
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(pilot);
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Follow));
            Assert.That(duty.FollowTarget, Is.EqualTo(target));
            Assert.That(duty.FollowRange, Is.EqualTo(160));
        });
        await WaitUntil(() => Server.System<WGCrewObjectiveSystem>().Snapshot()
                .Single(item => item.Group == "evading-departure").Objectives.Count == 0,
            300, () => "The timed task did not finish after normal flight resumed.");
    }

    /// <summary>Alerts during automatic departure preserve the intended destination and formation.</summary>
    [TestCase(WGPilotOrder.GoTo, false)]
    [TestCase(WGPilotOrder.GoTo, true)]
    [TestCase(WGPilotOrder.Loiter, false)]
    [TestCase(WGPilotOrder.Loiter, true)]
    [TestCase(WGPilotOrder.Follow, false)]
    [TestCase(WGPilotOrder.Follow, true)]
    [TestCase(WGPilotOrder.Dock, false)]
    [TestCase(WGPilotOrder.Dock, true)]
    public async Task CaptainResumesAutomaticDeparture(WGPilotOrder order, bool undockedDuringAlert)
    {
        var (deck, station, ownDock, _) = await CreateDockingPair(gravity: true);
        var destination = await CreateDeck(new Vector2(300, 300), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            var docking = Server.System<DockingSystem>();
            var config = docking.GetDockingConfig(deck, station);
            Assert.That(config, Is.Not.Null);
            Server.System<ShuttleSystem>().FTLDock((deck, SEntMan.GetComponent<TransformComponent>(deck)), config!);
            Assert.That(docking.AreGridsDocked(deck, station), Is.True);

            var crew = Server.System<WGCrewSystem>();
            var captain = crew.SpawnCrewman(WGCrewRoles.Captain,
                new EntityCoordinates(deck, new Vector2(1.5f)), "departure")!.Value;
            var pilot = crew.SpawnCrewman(WGCrewRoles.Pilot,
                new EntityCoordinates(deck, new Vector2(2.5f)), "departure")!.Value;
            SEntMan.GetComponent<HTNComponent>(captain).Enabled = false;
            SEntMan.GetComponent<HTNComponent>(pilot).Enabled = false;
            var flight = Server.System<WGPilotDutySystem>();
            var waypoint = new EntityCoordinates(MapData.MapUid, new Vector2(400, 500));
            var center = new EntityCoordinates(destination, Vector2.Zero);
            switch (order)
            {
                case WGPilotOrder.GoTo: flight.GoTo(pilot, new() { waypoint }); break;
                case WGPilotOrder.Loiter: flight.Loiter(pilot, center, 180); break;
                case WGPilotOrder.Follow: flight.Escort(pilot, destination, 120); break;
                case WGPilotOrder.Dock: flight.Dock(pilot, destination); break;
            }
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(pilot);
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Undock));
            Assert.That(duty.ResumeOrder, Is.EqualTo(order));
            var formation = duty.EscortOffset;
            var slot = duty.EscortSlot;

            var alert = new WGCrewAlertEvent(deck, "departure", System.Array.Empty<EntityUid>());
            SEntMan.EventBus.RaiseLocalEvent(deck, ref alert, true);
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Hold));
            if (undockedDuringAlert)
                docking.Undock((ownDock, SEntMan.GetComponent<DockingComponent>(ownDock)));

            // Evasion can replace the temporary flight destination before the captain restores it.
            duty.LoiterCenter = new EntityCoordinates(station, Vector2.One);
            duty.LoiterRadius = 300;
            var clear = new WGCrewAlertClearedEvent(deck, "departure");
            SEntMan.EventBus.RaiseLocalEvent(deck, ref clear, true);
            Assert.That(duty.Orders, Is.EqualTo(undockedDuringAlert ? order : WGPilotOrder.Undock));
            Assert.That(duty.ResumeOrder, Is.EqualTo(undockedDuringAlert ? (WGPilotOrder?) null : order));
            Assert.That(duty.OrdersCompleted, Is.False);
            switch (order)
            {
                case WGPilotOrder.GoTo:
                    Assert.That(undockedDuringAlert ? duty.Waypoints : duty.ResumeWaypoints, Is.EqualTo(new[] { waypoint }));
                    break;
                case WGPilotOrder.Loiter:
                    Assert.That(duty.LoiterCenter, Is.EqualTo(center));
                    Assert.That(duty.LoiterRadius, Is.EqualTo(180));
                    break;
                case WGPilotOrder.Follow:
                    Assert.That(duty.FollowTarget, Is.EqualTo(destination));
                    Assert.That(duty.EscortOffset, Is.EqualTo(formation));
                    Assert.That(duty.EscortSlot, Is.EqualTo(slot));
                    break;
                case WGPilotOrder.Dock:
                    Assert.That(duty.DockTarget, Is.EqualTo(destination));
                    break;
            }
        });
    }
}
