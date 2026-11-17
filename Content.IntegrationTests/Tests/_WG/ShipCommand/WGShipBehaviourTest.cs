#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WG.CargoLoad;
using Content.IntegrationTests.Tests._WG.ShipAccess;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ShipCommand;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.ShipCommand;

/// <summary>
/// The crew's standing orders from the command console: the owner sets them and a guest can't; marines, pilot, radio
/// officer and gunnery take them, as does whoever is staffed afterwards; a preset carries them; and gunners holding to
/// "defend the docks" answer a shot at a ship docked to theirs.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipBehaviourSystem))]
public sealed class WGShipBehaviourTest
{
    [Test]
    public async Task ConsoleSetsTheCrewsStandingOrders()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var consoles = entMan.System<WGShipConsoleSystem>();
        var roster = entMan.System<WGShipRosterSystem>();
        var presets = entMan.System<WGShipPresetSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var routine = entMan.System<WGCrewRoutineSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WGShipAccessComponent>(grid);
            entMan.EnsureComponent<WGShipCommandComponent>(grid);
            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(entMan, deed, grid);
            var (guest, _) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 3);
            var mindSys = entMan.System<SharedMindSystem>();
            var mind = mindSys.CreateMind(null, "Random Stranger");
            mindSys.TransferTo(mind, guest, mind: mind.Comp);

            entMan.System<SharedMapSystem>().SetTile(grid, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            entMan.SpawnEntity("WGShipCrewPostMarine", map.GridCoords);
            entMan.SpawnEntity("WGShipCrewPostMarine", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            entMan.SpawnEntity("WGShipCrewPostPilot", map.GridCoords);
            entMan.SpawnEntity("WGShipCrewPostRadioOperator", map.GridCoords);
            var ship = new Entity<WGShipRosterComponent>(grid, entMan.AddComponent<WGShipRosterComponent>(grid));
            roster.ReadPosts(ship);
            roster.Staff(ship, null, null);
            var console = entMan.SpawnEntity("WGComputerShipCommand", map.GridCoords);

            EntityUid Staffed(string role) => ship.Comp.Slots.Values.First(s => s.Role.Id == role).Occupant!.Value;
            var pilot = entMan.GetComponent<WGPilotDutyComponent>(Staffed("WGCrewPilot"));
            var radio = entMan.GetComponent<WGRadioOperatorComponent>(Staffed("WGCrewRadioOperator"));
            var marine = Staffed("WGCrewMarine");

            Assert.That(consoles.SetOrders(console, guest, WGShipMarineOrders.Posts, null, null, null), Is.False, "A guest gives no orders.");
            Assert.That(ship.Comp.MarineOrders, Is.EqualTo(WGShipMarineOrders.Patrol));

            // Stand at posts: an empty route, which the routine walks nowhere from.
            Assert.That(consoles.SetOrders(console, owner, WGShipMarineOrders.Posts, null, null, null), Is.True);
            var orders = entMan.GetComponent<WGCrewPatrolOrdersComponent>(marine);
            Assert.That(orders.Route, Is.Empty);
            Assert.That(routine.NextPatrolPoint(marine, grid, entMan.GetComponent<TransformComponent>(marine)), Is.Null, "He stays at his post.");

            // Walk the posts: each marine goes round both, starting from the other's.
            Assert.That(consoles.SetOrders(console, owner, WGShipMarineOrders.Rounds, null, null, null), Is.True);
            Assert.That(orders.Route, Has.Count.EqualTo(2));
            var xform = entMan.GetComponent<TransformComponent>(marine);
            var first = routine.NextPatrolPoint(marine, grid, xform);
            var second = routine.NextPatrolPoint(marine, grid, xform);
            Assert.That(first, Is.Not.EqualTo(second), "He moves on along the round.");

            Assert.That(consoles.SetOrders(console, owner, WGShipMarineOrders.Patrol, null, null, null), Is.True);
            Assert.That(entMan.HasComponent<WGCrewPatrolOrdersComponent>(marine), Is.False, "Patrolling is the routine's own wandering.");

            Assert.That(consoles.SetOrders(console, owner, null, null, WGShipPilotStance.FaceThreats, null), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(pilot.FaceThreats, Is.True);
                Assert.That(pilot.ReactToAttacks, Is.False);
            });
            Assert.That(consoles.SetOrders(console, owner, null, null, WGShipPilotStance.Run, null), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(pilot.FaceThreats, Is.False);
                Assert.That(pilot.ReactToAttacks, Is.True);
                Assert.That(pilot.FleeAttackers, Is.True);
            });

            Assert.That(consoles.SetOrders(console, owner, null, null, null, "  Wolf [bold]Six[/bold]\n"), Is.True);
            Assert.That(radio.Callsign, Is.EqualTo(ship.Comp.Callsign));
            Assert.That(radio.Callsign, Does.Not.Contain("\n"), "A callsign is one line.");
            Assert.That(radio.Callsign, Does.Not.Contain("["), "A callsign carries no markup.");

            Assert.That(consoles.SetOrders(console, owner, null, WGGunnerPolicy.WhenFiredOn, null, null), Is.True);
            Assert.That(entMan.System<WGGunnerDutySystem>().HoldsFireByPolicy(grid), Is.True);

            // Whoever is staffed afterwards takes the same orders.
            var pilotSlot = ship.Comp.Slots.Values.First(s => s.Role.Id == "WGCrewPilot");
            entMan.DeleteEntity(pilotSlot.Occupant!.Value);
            Assert.That(roster.Staff(ship, pilotSlot.Id, null), Is.EqualTo(1));
            Assert.That(entMan.GetComponent<WGPilotDutyComponent>(pilotSlot.Occupant!.Value).FleeAttackers, Is.True, "The new pilot runs too.");

            // A preset carries them, and loading one passes them to the crew aboard.
            var saved = presets.Capture(grid);
            Assert.That(saved.GunnerPolicy, Is.EqualTo(WGGunnerPolicy.WhenFiredOn));
            saved = WGShipPresetSystem.Deserialize(WGShipPresetSystem.Serialize(saved))!;
            saved.GunnerPolicy = WGGunnerPolicy.Hostiles;
            saved.PilotStance = WGShipPilotStance.Hold;
            presets.Apply(grid, saved, owner, "test");
            Assert.Multiple(() =>
            {
                Assert.That(entMan.System<WGGunnerDutySystem>().HoldsFireByPolicy(grid), Is.False);
                Assert.That(entMan.GetComponent<WGPilotDutyComponent>(pilotSlot.Occupant!.Value).ReactToAttacks, Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Gunners defending their docks treat a shot at a docked ship as one at their own; "when fired on" does not.</summary>
    [Test]
    public async Task DefendingTheDocksAnswersAShotAtADockedShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var alerts = entMan.System<WGCrewAlertSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var host = CargoDeck.Create(entMan, map, new Vector2(100, 0));
            var guest = CargoDeck.Create(entMan, map, new Vector2(112, 0));
            var attacker = CargoDeck.Create(entMan, map, new Vector2(100, 120));
            foreach (var ship in new[] { host, guest, attacker })
                entMan.EnsureComponent<ShuttleComponent>(ship);
            CargoDeck.Dock(entMan, CargoDeck.AddPort(entMan, host, new Vector2(9.5f, 0.5f), -90f),
                CargoDeck.AddPort(entMan, guest, new Vector2(0.5f, 0.5f), 90f));

            var coords = new EntityCoordinates(host, new Vector2(0.5f, 0.5f));
            var crew = entMan.System<WGCrewSystem>().SpawnCrewman("WGCrewGunner", coords, "docktest")!.Value;
            var policy = entMan.EnsureComponent<WGGunnerPolicyComponent>(host);

            policy.Policy = WGGunnerPolicy.WhenFiredOn;
            Hit(entMan, guest, attacker);
            Assert.That(alerts.FiredShipGuns(host, "docktest", attacker), Is.False, "A shot at the guest is no shot at this ship.");

            policy.Policy = WGGunnerPolicy.DefendDocked;
            Hit(entMan, guest, attacker);
            Assert.That(alerts.FiredShipGuns(host, "docktest", attacker), Is.True, "Defending the docks, it is.");
            entMan.DeleteEntity(crew);
        });

        await pair.CleanReturnAsync();
    }

    private static void Hit(IEntityManager entMan, EntityUid grid, EntityUid attacker)
    {
        var hit = new WGCrewHullHitEvent(grid, attacker);
        entMan.EventBus.RaiseEvent(EventSource.Local, ref hit);
    }
}
