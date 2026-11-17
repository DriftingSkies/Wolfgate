#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.NpcCrew;
using Content.Shared.NPC.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    private const string NavigationScenario = "WGTestCrewNavigationScenario";

    [TestPrototypes]
    private const string NavigationScenarioPrototype = @"
- type: wgCrewNavigation
  id: WGTestCrewNavigationScenario
  name: wg-crew-setup-navigation-defaults
  settings:
    cruiseSpeed: 18
    circleSpeed: 2
    attackRange: 420
    navigationClearance: 80
";

    /// <summary>Scenario prototype settings remain independent of mission edits and every spawned crew.</summary>
    [Test]
    public async Task CrewNavigationProfilesStayIndependentAcrossShips()
    {
        await AddAtmosphere();
        var first = await CreateDeck(new Vector2(20, 20), 7, gravity: true);
        var second = await CreateDeck(new Vector2(80, 20), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            FillCrewTestAir(first);
            FillCrewTestAir(second);
            var profile = Server.ResolveDependency<IPrototypeManager>().Index<WGCrewNavigationProfilePrototype>(NavigationScenario);
            Assert.That(profile.Name.Id, Is.EqualTo("wg-crew-setup-navigation-defaults"));
            Assert.That(profile.Settings.CruiseSpeed, Is.EqualTo(18));
            Assert.That(profile.Settings.CircleSpeed, Is.EqualTo(2));
            Assert.That(profile.Settings.AttackRange, Is.EqualTo(420));
            Assert.That(profile.Settings.FollowSpeed, Is.EqualTo(new WGCrewNavigationSettings().FollowSpeed),
                "Unspecified scenario values retain their defaults.");
            var mission = new WGCrewMission { Group = "profile-one", Navigation = profile.Settings.Clone() };
            var other = new WGCrewMission { Group = "profile-two", Navigation = profile.Settings.Clone() };
            other.Navigation.CruiseSpeed = 7;
            var posts = new List<WGCrewSetupPost>
            {
                new() { Role = WGCrewRoles.Pilot.Id, Position = new Vector2(2.5f) },
                new() { Role = WGCrewRoles.Deckhand.Id, Position = new Vector2(4.5f) },
            };
            var setup = Server.System<WGCrewSetupSystem>();
            Assert.That(setup.TrySpawn(first, posts, mission, out var one), Is.True);
            Assert.That(setup.TrySpawn(second, posts, other, out var two), Is.True);
            foreach (var member in one.Concat(two))
                SEntMan.GetComponent<HTNComponent>(member).Enabled = false;
            mission.Navigation.CruiseSpeed = 26;
            Assert.That(SEntMan.GetComponent<WGPilotDutyComponent>(one[0]).Navigation.CruiseSpeed, Is.EqualTo(18),
                "Editing a caller's mission must not mutate a previously spawned pilot.");
            Assert.That(setup.TryApplyMission(first, mission), Is.True);
            foreach (var member in one)
                Assert.That(SEntMan.GetComponent<WGCrewComponent>(member).Navigation.CruiseSpeed, Is.EqualTo(26));
            foreach (var member in two)
                Assert.That(SEntMan.GetComponent<WGCrewComponent>(member).Navigation.CruiseSpeed, Is.EqualTo(7));
            var firstPilot = SEntMan.GetComponent<WGPilotDutyComponent>(one[0]);
            firstPilot.Navigation.CruiseSpeed = 31;
            Assert.That(SEntMan.GetComponent<WGCrewComponent>(one[0]).Navigation.CruiseSpeed, Is.EqualTo(26));
            Assert.That(SEntMan.GetComponent<WGCrewComponent>(one[1]).Navigation.CruiseSpeed, Is.EqualTo(26));
            Assert.That(SEntMan.GetComponent<WGPilotDutyComponent>(two[0]).Navigation.CruiseSpeed, Is.EqualTo(7));
            Assert.That(mission.Navigation.CruiseSpeed, Is.EqualTo(26));
            Assert.That(profile.Settings.CruiseSpeed, Is.EqualTo(18), "A reusable scenario prototype must remain immutable.");
        });
    }

    /// <summary>Server entry points reject invalid profiles before changing crew, ship factions or queued work.</summary>
    [TestCase("nan")]
    [TestCase("negative")]
    [TestCase("hold")]
    public async Task CrewServerRejectsInvalidNavigationAtomically(string invalid)
    {
        await AddAtmosphere();
        var deck = await CreateDeck(new Vector2(20, 20), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            FillCrewTestAir(deck);
            var mission = new WGCrewMission { Group = "invalid-navigation" };
            var posts = new List<WGCrewSetupPost> { new() { Role = WGCrewRoles.Pilot.Id, Position = new Vector2(2.5f) } };
            var setup = Server.System<WGCrewSetupSystem>();
            Assert.That(setup.TrySpawn(deck, posts, mission, out var created), Is.True);
            var pilot = created.Single();
            SEntMan.GetComponent<HTNComponent>(pilot).Enabled = false;
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(pilot);
            var settings = duty.Navigation;
            var anchor = duty.HoldPosition;
            var factions = SEntMan.GetComponent<NpcFactionMemberComponent>(deck).Factions.ToArray();
            var queue = Server.System<WGCrewObjectiveSystem>();
            Assert.That(queue.SetQueue(deck, mission.Group,
                new List<WGCrewObjective> { new() { Kind = WGCrewObjectiveKind.Hold, Duration = 100 } }), Is.True);
            mission.Order = WGPilotOrder.GoTo;
            mission.Destination = new Vector2(1000);
            mission.Faction = "SimpleHostile";
            switch (invalid)
            {
                case "nan": mission.Navigation.CruiseSpeed = float.NaN; break;
                case "negative": mission.Navigation.NavigationClearance = -1; break;
                case "hold": mission.Navigation.HoldReturnRange = mission.Navigation.HoldRange + 1; break;
            }
            Assert.That(setup.TrySpawn(deck, posts, mission, out var rejected), Is.False);
            Assert.That(rejected, Is.Empty);
            Assert.That(setup.TryApplyMission(deck, mission), Is.False);
            Assert.That(Server.System<WGPilotDutySystem>().SetNavigation(pilot, mission.Navigation), Is.False);
            Assert.That(duty.Navigation, Is.SameAs(settings));
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Hold));
            Assert.That(duty.HoldPosition, Is.EqualTo(anchor));
            Assert.That(SEntMan.GetComponent<NpcFactionMemberComponent>(deck).Factions, Is.EquivalentTo(factions));
            Assert.That(queue.Snapshot().Single(row => row.Group == mission.Group).Objectives, Has.Count.EqualTo(1),
                "An invalid mission must not cancel the current queue before validation.");
        });
    }

    /// <summary>Live profile edits update steering immediately while preserving elapsed task time and hold anchors.</summary>
    [TestCase(WGCrewObjectiveKind.Circle)]
    [TestCase(WGCrewObjectiveKind.Attack)]
    public async Task CrewLiveNavigationEditsPreserveOrders(WGCrewObjectiveKind kind)
    {
        var deck = await CreateDeck(new Vector2(20, 20), 7, gravity: true);
        var target = await CreateDeck(new Vector2(500, 20), 9, gravity: true);
        EntityUid pilot = default, helm = default;
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            helm = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(3.5f)));
            pilot = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Pilot,
                new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)), "live-navigation")!.Value;
        });
        await WaitUntil(() => Server.System<WGPilotDutySystem>().IsAtHelm(pilot),
            600, () => DescribePilot(pilot, helm));
        await Server.WaitAssertion(() =>
        {
            var queue = Server.System<WGCrewObjectiveSystem>();
            var pilots = Server.System<WGPilotDutySystem>();
            var item = new WGCrewObjective { Kind = kind, Target = SEntMan.GetNetEntity(target), Range = 150, Duration = 10 };
            Assert.That(queue.SetQueue(deck, "live-navigation", new List<WGCrewObjective>
                { item, new() { Kind = WGCrewObjectiveKind.Hold } }), Is.True);
            queue.Update(4f);
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(pilot);
            var anchor = duty.HoldPosition;
            var center = duty.LoiterCenter;
            var steerer = SEntMan.GetComponent<ShipSteererComponent>(pilot);
            var previousRange = steerer.Range;
            var limits = duty.Navigation.Clone();
            limits.CircleSpeed = 2;
            limits.AttackSpeed = 3;
            limits.AttackRange = 600;
            limits.NavigationClearance = 200;
            Assert.That(pilots.SetNavigation(pilot, limits), Is.True);
            Assert.That(SEntMan.GetComponent<ShipSteererComponent>(pilot), Is.SameAs(steerer));
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Loiter));
            Assert.That(duty.OrbitKind, Is.EqualTo(kind));
            Assert.That(duty.LoiterCenter, Is.EqualTo(center));
            Assert.That(duty.HoldPosition, Is.EqualTo(anchor));
            Assert.That(steerer.InRangeMaxSpeed, Is.EqualTo(kind == WGCrewObjectiveKind.Circle ? 2 : 3));
            Assert.That(steerer.Range, Is.GreaterThan(previousRange));
            Assert.That(duty.LoiterRadius, Is.EqualTo(kind == WGCrewObjectiveKind.Circle ? 150 : 600));
            limits.CircleSpeed = 99;
            limits.AttackRange = 999;
            Assert.That(duty.Navigation.CircleSpeed, Is.EqualTo(2), "Live edits must also clone the caller's settings.");
            Assert.That(duty.Navigation.AttackRange, Is.EqualTo(600));
            queue.Update(6f);
            Assert.That(queue.Snapshot().Single(row => row.Group == "live-navigation").Objectives
                .Select(order => order.Kind), Is.EqualTo(new[] { WGCrewObjectiveKind.Hold }),
                "Four seconds before the edit and six after it must finish the same ten-second task.");
            anchor = duty.HoldPosition;
            var heading = duty.HoldHeading;
            var transform = Server.System<SharedTransformSystem>();
            transform.SetWorldPosition(deck, transform.GetWorldPosition(deck) + new Vector2(9, 0));
            var holdSettings = duty.Navigation.Clone();
            holdSettings.HoldRange = 8;
            Assert.That(pilots.SetNavigation(pilot, holdSettings), Is.True);
            Assert.That(duty.HoldPosition, Is.EqualTo(anchor), "A profile edit must not recapture a drifting ship's hold station.");
            Assert.That(duty.HoldHeading, Is.EqualTo(heading));
            Assert.That(SEntMan.GetComponent<ShipSteererComponent>(pilot).Coordinates, Is.EqualTo(anchor));
        });
    }
}
