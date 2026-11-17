#nullable enable
using System.Numerics;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>A captain killed mid-evasion leaves the pilot to restore the saved course on all-clear.</summary>
    [Test]
    public async Task PilotRestoresCourseAfterCaptainDies()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var captain = ConvoyCrew(deck, "WGCrewCaptain", "fallen", new Vector2(1.5f));
            var pilot = ConvoyCrew(deck, "WGCrewPilot", "fallen", new Vector2(2.5f, 1.5f));
            var captains = Server.System<WGCaptainSystem>();
            var destination = new EntityCoordinates(deck, new Vector2(100, 100));
            Server.System<WGPilotDutySystem>().GoTo(pilot, new() { destination });
            var alert = new WGCrewAlertEvent(deck, "fallen", System.Array.Empty<EntityUid>());
            SEntMan.EventBus.RaiseLocalEvent(deck, ref alert, true);
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(pilot);
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Hold));

            Server.System<MobStateSystem>().ChangeMobState(captain, MobState.Dead);
            captains.Update(0f);
            Assert.That(captains.IsCourseSuspended(pilot), Is.True, "The pilot keeps the course of a fallen captain.");
            // A further alert must not save the evasion as the course.
            SEntMan.EventBus.RaiseLocalEvent(deck, ref alert, true);

            var clear = new WGCrewAlertClearedEvent(deck, "fallen");
            SEntMan.EventBus.RaiseLocalEvent(deck, ref clear, true);
            Assert.That(captains.IsCourseSuspended(pilot), Is.False);
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.GoTo));
            Assert.That(duty.Waypoints, Is.EqualTo(new[] { destination }));
        });
    }

    /// <summary>A saved escort whose leader is gone by all-clear ends in a hold, not the evasive orbit.</summary>
    [Test]
    public async Task CaptainHoldsWhenSavedTargetIsGone()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var leader = await CreateDeck(new Vector2(100, 0), 5, gravity: true);
        var attacker = await CreateDeck(new Vector2(300, 0), 3, gravity: true);
        EntityUid pilot = default;
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WGCrewCaptain", "orphan", new Vector2(1.5f));
            pilot = ConvoyCrew(deck, "WGCrewPilot", "orphan", new Vector2(2.5f, 1.5f));
            Server.System<WGPilotDutySystem>().Follow(pilot, leader, 100);
            Server.System<WGCrewAlertSystem>().ReportShipThreat(deck, "orphan", attacker);
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(pilot);
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Loiter));
            Assert.That(duty.LoiterCenter!.Value.EntityId, Is.EqualTo(attacker));
            SEntMan.DeleteEntity(leader);
        });
        await WaitUntil(() => !Server.System<WGCaptainSystem>().IsCourseSuspended(pilot),
            4000, () => "Captain did not end the evasion after the hull alert expired.");
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<WGPilotDutyComponent>(pilot).Orders, Is.EqualTo(WGPilotOrder.Hold)));
    }

    /// <summary>A hold for boarders turns into an evasive orbit once a ship attacks, and holds again when it is gone.</summary>
    [Test]
    public async Task CaptainReactsToShipThreatAfterBoarderAlert()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var attacker = await CreateDeck(new Vector2(300, 0), 3, gravity: true);
        EntityUid pilot = default;
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WGCrewCaptain", "boarded", new Vector2(1.5f));
            pilot = ConvoyCrew(deck, "WGCrewPilot", "boarded", new Vector2(2.5f, 1.5f));
            Server.System<WGPilotDutySystem>().GoTo(pilot, new() { new EntityCoordinates(deck, new Vector2(100, 100)) });
            // Boarders alone: no hostile ship, so the pilot holds.
            var boarders = new WGCrewAlertEvent(deck, "boarded", System.Array.Empty<EntityUid>());
            SEntMan.EventBus.RaiseLocalEvent(deck, ref boarders, true);
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(pilot);
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Hold));

            Server.System<WGCrewAlertSystem>().ReportShipThreat(deck, "boarded", attacker);
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Loiter));
            Assert.That(duty.LoiterCenter!.Value.EntityId, Is.EqualTo(attacker));
            Assert.That(Server.System<WGCaptainSystem>().IsCourseSuspended(pilot), Is.True);
            SEntMan.DeleteEntity(attacker);
        });
        await WaitUntil(() => SEntMan.GetComponent<WGPilotDutyComponent>(pilot).Orders == WGPilotOrder.Hold,
            180, () => "The pilot kept orbiting a deleted attacker.");
        await Server.WaitAssertion(() =>
            Assert.That(Server.System<WGCaptainSystem>().IsCourseSuspended(pilot), Is.True,
                "Re-aiming the evasion keeps the saved course."));
    }

    /// <summary>A crewman held off the ship delays an undock only for the configured wait.</summary>
    [Test]
    public async Task AbsentCrewDelayUndockOnlyForAWhile()
    {
        var (deck, station, _, _) = await CreateDockingPair(gravity: true);
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
            Assert.That(docking.AreGridsDocked(deck, station), Is.True);
            pilot = ConvoyCrew(deck, "WGCrewPilot", "absent", new Vector2(2.5f, 3.5f));
            // Posted aboard, but held on the station.
            var absent = ConvoyCrew(station, "WGCrewDeckhand", "absent", new Vector2(2.5f));
            SEntMan.GetComponent<WGCrewComponent>(absent).Post = new EntityCoordinates(deck, new Vector2(1.5f));
        });
        await WaitUntil(() => Server.System<WGPilotDutySystem>().TryFindHelm(pilot, out var available) && available == helm,
            120, () => DescribePilot(pilot, helm));
        await Server.WaitAssertion(() =>
        {
            var flight = Server.System<WGPilotDutySystem>();
            Assert.That(flight.TryTakeHelm(pilot, helm), Is.True, DescribePilot(pilot, helm));
            SEntMan.GetComponent<WGPilotDutyComponent>(pilot).AbsentCrewWait = 1f;
            flight.Undock(pilot);
            Assert.That(SEntMan.GetComponent<WGPilotDutyComponent>(pilot).Orders, Is.EqualTo(WGPilotOrder.Undock));
        });
        await RunTicks(20);
        await Server.WaitAssertion(() =>
            Assert.That(Server.System<DockingSystem>().AreGridsDocked(deck, station), Is.True,
                "The pilot waits for absent crew first."));
        await WaitUntil(() => !Server.System<DockingSystem>().AreGridsDocked(deck, station),
            300, () => $"Absent crew kept the ship docked: {DescribePilot(pilot, helm)}");
    }
}
