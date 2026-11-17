#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>A temporary evasive orbit restores the held station instead of capturing the displaced ship.</summary>
    [Test]
    public async Task CaptainRestoresHeldStationAfterEvasiveOrbit()
    {
        var ship = await PoweredNavigationShip(new Vector2(500, 500));
        var threat = await CreateDeck(new Vector2(800, 800), 7, true);
        EntityCoordinates anchor = default;
        Angle heading = default;
        await Server.WaitAssertion(() =>
        {
            var captain = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Captain,
                new EntityCoordinates(ship.Grid, new Vector2(1.5f)), "navigation")!.Value;
            SEntMan.GetComponent<HTNComponent>(captain).Enabled = false;
            var objectives = Server.System<WGCrewObjectiveSystem>();
            Assert.That(objectives.SetQueue(ship.Grid, "navigation", new()
            {
                new WGCrewObjective { Kind = WGCrewObjectiveKind.Hold, Duration = 120 },
            }), Is.True);
            objectives.Update(.5f);
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(ship.Pilot);
            anchor = duty.HoldPosition!.Value;
            heading = duty.HoldHeading;
            Server.System<WGCrewAlertSystem>().ReportShipThreat(ship.Grid, "navigation", threat);
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Loiter));
            Assert.That(Server.System<WGCaptainSystem>().IsCourseSuspended(ship.Pilot), Is.True);
        });
        await WaitUntil(() => !Server.System<WGCaptainSystem>().IsCourseSuspended(ship.Pilot),
            4000, () => DescribePilot(ship.Pilot, ship.Helm));
        await Server.WaitAssertion(() =>
        {
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(ship.Pilot);
            var position = Server.System<SharedTransformSystem>().GetWorldPosition(ship.Grid);
            Assert.That(Vector2.Distance(position, anchor.Position), Is.GreaterThan(10),
                "The ship must physically leave its station during the evasive orbit.");
            Assert.That(duty.Orders, Is.EqualTo(WGPilotOrder.Hold));
            Assert.That(duty.HoldPosition, Is.EqualTo(anchor));
            Assert.That(duty.HoldHeading, Is.EqualTo(heading));
            Assert.That(SEntMan.GetComponent<ShipSteererComponent>(ship.Pilot).Coordinates, Is.EqualTo(anchor));
            Assert.That(Server.System<WGCrewObjectiveSystem>().Snapshot().Single(row => row.Group == "navigation")
                .Objectives.Single().Kind, Is.EqualTo(WGCrewObjectiveKind.Hold));
        });
    }
}
