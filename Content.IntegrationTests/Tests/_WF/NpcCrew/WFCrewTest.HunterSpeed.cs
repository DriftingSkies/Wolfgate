#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    private WFCrewNavigationSettings Profile(string id)
    {
        return Server.ResolveDependency<IPrototypeManager>().Index<WFCrewNavigationProfilePrototype>(id).Settings.Clone();
    }

    /// <summary>The hunter profile sets no speed cap and is valid; the raider's holds travel to its cruise speed.</summary>
    [TestCase("WFCrewNavigationHunter", false)]
    [TestCase("WFCrewNavigationRaider", true)]
    public async Task HunterProfileSetsNoSpeedCap(string profile, bool capped)
    {
        var ship = await PoweredNavigationShip(new Vector2(3000, 3000));
        await Server.WaitAssertion(() =>
        {
            var settings = Profile(profile);
            Assert.That(settings.IsValid(), Is.True);
            Assert.That(Server.System<WFPilotDutySystem>().SetNavigation(ship.Pilot, settings), Is.True);
            Assert.That(Server.System<WFCrewObjectiveSystem>().SetQueue(ship.Grid, "navigation", new List<WFCrewObjective>
            {
                new() { Kind = WFCrewObjectiveKind.GoTo, Position = new Vector2(30000, 3000), Range = 100 },
            }), Is.True);
        });
        await RunTicks(30);
        await Server.WaitAssertion(() =>
        {
            var ev = new GetShuttleInputsEvent(1f / 30f, ship.Grid);
            SEntMan.EventBus.RaiseLocalEvent(ship.Pilot, ref ev, true);
            Assert.That(ev.GotInput, Is.True, "The pilot is steering.");
            if (capped)
                Assert.That(ev.SetMaxVelocity, Is.EqualTo(30f), "A raider's travel is capped at its cruise speed.");
            else
                Assert.That(ev.SetMaxVelocity, Is.Null, "A hunter's travel is capped by nothing but the hull.");
        });
    }

    /// <summary>With no cruise speed set, a travel estimate counts on the hull's own top speed.</summary>
    [Test]
    public async Task UncappedTravelBudgetCountsOnTheHullsTopSpeed()
    {
        var ship = await PoweredNavigationShip(new Vector2(3500, 3500));
        await Server.WaitAssertion(() =>
        {
            var pilots = Server.System<WFPilotDutySystem>();
            var duty = SEntMan.GetComponent<WFPilotDutyComponent>(ship.Pilot);
            SEntMan.GetComponent<ShuttleComponent>(ship.Grid).BaseMaxLinearVelocity = 42f;
            Assert.That(Server.System<WFPilotDutySystem>().SetNavigation(ship.Pilot, Profile("WFCrewNavigationHunter")), Is.True);
            Assert.That(pilots.TravelSpeed(duty, ship.Grid), Is.EqualTo(42f), "The hull's own limit.");
            Assert.That(Server.System<WFPilotDutySystem>().SetNavigation(ship.Pilot, Profile("WFCrewNavigationRaider")), Is.True);
            Assert.That(pilots.TravelSpeed(duty, ship.Grid), Is.EqualTo(30f), "A set cruise speed still rules.");
        });
    }

    /// <summary>A pilot with the hunter profile flies faster than one held to a low cruise speed over the same run.</summary>
    [Test]
    public async Task HunterOutrunsACappedPilot()
    {
        var hunter = await PoweredNavigationShip(new Vector2(4000, 4000));
        var capped = await PoweredNavigationShip(new Vector2(4000, 4400));
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFPilotDutySystem>().SetNavigation(hunter.Pilot, Profile("WFCrewNavigationHunter")), Is.True);
            var slow = Profile("WFCrewNavigationHunter");
            slow.CruiseSpeed = 6f;
            Assert.That(Server.System<WFPilotDutySystem>().SetNavigation(capped.Pilot, slow), Is.True);
            foreach (var (grid, y) in new[] { (hunter.Grid, 4015.5f), (capped.Grid, 4415.5f) })
            {
                Assert.That(Server.System<WFCrewObjectiveSystem>().SetQueue(grid, "navigation", new List<WFCrewObjective>
                {
                    new() { Kind = WFCrewObjectiveKind.GoTo, Position = new Vector2(40000, y), Range = 100 },
                }), Is.True);
            }
        });
        var hunterPeak = 0f;
        var cappedPeak = 0f;
        for (var i = 0; i < 20; i++)
        {
            await RunTicks(30);
            await Server.WaitAssertion(() =>
            {
                hunterPeak = MathF.Max(hunterPeak, SEntMan.GetComponent<PhysicsComponent>(hunter.Grid).LinearVelocity.Length());
                cappedPeak = MathF.Max(cappedPeak, SEntMan.GetComponent<PhysicsComponent>(capped.Grid).LinearVelocity.Length());
            });
        }

        TestContext.Out.WriteLine($"hunterPeak={hunterPeak:0.00} cappedPeak={cappedPeak:0.00}");
        Assert.That(cappedPeak, Is.LessThan(7f), "The capped pilot holds its cruise speed.");
        Assert.That(hunterPeak, Is.GreaterThan(cappedPeak + 3f), "The hunter is not held to it.");
    }
}
