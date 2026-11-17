#nullable enable
using Content.IntegrationTests.Tests._WG.EngineCompat;
using System.Numerics;
using System.Reflection;
using Robust.Client.UserInterface.Controls;
using Content.Client._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Content.Server.Power.Components;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Escorts take separate hull-safe slots which translate and rotate with their leader.</summary>
    [Test]
    public async Task EscortsKeepFormationWhenLeaderTurns()
    {
        var leader = await CreateDeck(new Vector2(200, 200), 9, gravity: true);
        var ships = new[] { await CreateDeck(new Vector2(100, 100), 5, true), await CreateDeck(new Vector2(300, 100), 5, true) };
        var pilots = new EntityUid[2];
        var helms = new EntityUid[2];
        await Server.WaitPost(() =>
        {
            for (var i = 0; i < 2; i++)
            {
                SEntMan.EnsureComponent<ShuttleComponent>(ships[i]);
                helms[i] = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(ships[i], new Vector2(2.5f)));
                pilots[i] = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Pilot, new EntityCoordinates(ships[i], new Vector2(2.5f, 1.5f)), "escort")!.Value;
            }
        });
        await WaitUntil(() => SEntMan.GetComponent<WGPilotDutyComponent>(pilots[0]).AtHelm
            && SEntMan.GetComponent<WGPilotDutyComponent>(pilots[1]).AtHelm, 300,
            () => $"{DescribePilot(pilots[0], helms[0])}; {DescribePilot(pilots[1], helms[1])}");
        await Server.WaitAssertion(() =>
        {
            var flight = Server.System<WGPilotDutySystem>();
            for (var i = 0; i < 2; i++)
            {
                Assert.That(flight.TryTakeHelm(pilots[i], helms[i]), Is.True);
                flight.Escort(pilots[i], leader, 1);
            }
            var one = SEntMan.GetComponent<WGPilotDutyComponent>(pilots[0]);
            var two = SEntMan.GetComponent<WGPilotDutyComponent>(pilots[1]);
            Assert.That(one.EscortSlot, Is.Not.EqualTo(two.EscortSlot));
            Assert.That((one.EscortOffset!.Value - two.EscortOffset!.Value).Length(), Is.GreaterThan(40));
            var steering = SEntMan.GetComponent<Content.Server._Mono.NPC.HTN.ShipSteererComponent>(pilots[0]);
            Assert.That(steering.Coordinates.EntityId, Is.EqualTo(leader));
            var transforms = Server.System<SharedTransformSystem>();
            var before = transforms.ToMapCoordinates(steering.Coordinates).Position;
            transforms.SetWorldRotation(leader, Angle.FromDegrees(90));
            var after = transforms.ToMapCoordinates(steering.Coordinates).Position;
            Assert.That((after - before).Length(), Is.GreaterThan(20));
            Assert.That(steering.Coordinates.Position, Is.EqualTo(one.EscortOffset.Value));
        });
    }

    /// <summary>The actual Dredger hull can physically mate with the actual Drillsite without FTL.</summary>
    [Test]
    public async Task DredgerDocksAtDrillsite()
    {
        EntityUid ship = default, site = default, pilot = default, helm = default;
        await Server.WaitAssertion(() =>
        {
            var loader = Server.System<MapLoaderSystem>();
            Assert.That(loader.TryLoadGrid(MapData.MapId, new ResPath("/SharedMaps/_WG/Shipyard/Shuttles/dredger.yml"), out var loadedShip,
                offset: new Vector2(2000, 2200)), Is.True);
            Assert.That(loader.TryLoadGrid(MapData.MapId, new ResPath("/Maps/_Mono/POI/derelictdrillsite.yml"), out var loadedSite,
                offset: new Vector2(2000, 2000)), Is.True);
            ship = loadedShip!.Value.Owner;
            site = loadedSite!.Value.Owner;
        });
        await RunTicks(120);
        await Server.WaitAssertion(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var transform))
            {
                if (transform.GridUid == ship) { helm = uid; break; }
            }
            Assert.That(helm.IsValid(), Is.True);
            var power = SEntMan.EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();
            while (power.MoveNext(out _, out var receiver, out var transform))
            {
                if (transform.GridUid == ship)
                    receiver.NeedsPower = false;
            }
            var thrusters = SEntMan.EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
            while (thrusters.MoveNext(out var uid, out var thrust, out var transform))
            {
                if (transform.GridUid == ship)
                    Server.System<ThrusterSystem>().EnableThruster(uid, thrust);
            }
            var planner = Server.System<WGCrewPlannerSystem>();
            var post = planner.Plan(ship).Find(entry => entry.Role == WGCrewRoles.Pilot);
            Assert.That(post.Coordinates.IsValid(SEntMan), Is.True, "Dredger has a safe helm post.");
            pilot = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Pilot, post.Coordinates, "dredger")!.Value;
            Assert.That(Server.System<WGPilotDutySystem>().TryPlanDock(ship, site, 40, out _), Is.True, "A pair of hull ports fits.");
            Server.System<WGPilotDutySystem>().Dock(pilot, site);
        });
        await WaitUntil(() => Server.System<DockingSystem>().AreGridsDocked(ship, site), 18000,
            () => $"{DescribePilot(pilot, helm)} phase={SEntMan.GetComponent<WGPilotDutyComponent>(pilot).DockPhase} attempts={SEntMan.GetComponent<WGPilotDutyComponent>(pilot).DockAttempts} position={SEntMan.GetComponent<TransformComponent>(ship).LocalPosition} angle={SEntMan.GetComponent<TransformComponent>(ship).LocalRotation} velocity={SEntMan.GetComponent<PhysicsComponent>(ship).LinearVelocity} turn={SEntMan.GetComponent<PhysicsComponent>(ship).AngularVelocity}");
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Server.System<WGPilotDutySystem>().GoTo(pilot, new() { new(MapData.MapUid, new Vector2(2200, 2400)) });
            Assert.That(SEntMan.GetComponent<WGPilotDutyComponent>(pilot).Orders, Is.EqualTo(WGPilotOrder.Undock));
        });
        await WaitUntil(() => !Server.System<DockingSystem>().AreGridsDocked(ship, site)
            && SEntMan.GetComponent<WGPilotDutyComponent>(pilot).Orders == WGPilotOrder.GoTo, 6000,
            () => $"Docked ship did not release and resume travel: {DescribePilot(pilot, helm)}");
    }

    [TestPrototypes]
    private const string FlightPrototypes = @"
- type: entity
  parent: Thruster
  id: WGTestCrewThruster
  components:
  - type: ApcPowerReceiver
    needsPower: false
  - type: Thruster
    requireSpace: false
- type: entity
  parent: Gyroscope
  id: WGTestCrewGyroscope
  components:
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <summary>The real client controls can construct and release their network subscriptions.</summary>
    [Test]
    public async Task CrewSetupWindowConstructs()
    {
        await Client.WaitAssertion(() =>
        {
            var window = new WGCrewSetupWindow();
            window.OpenCentered();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(WGCrewSetupWindow);
            var receive = type.GetMethod("Receive", flags)!;
            var order = (OptionButton) type.GetField("_order", flags)!.GetValue(window)!;
            var target = (OptionButton) type.GetField("_target", flags)!.GetValue(window)!;
            var targetLine = (BoxContainer) type.GetField("_targetLine", flags)!.GetValue(window)!;
            var source = new WGCrewSetupGrid(new NetEntity(101), "Ship");
            var destination = new WGCrewSetupGrid(new NetEntity(102), "Station");
            var crew = new WGCrewSetupCrew { Grid = source.Id, Group = "test", Members = 2, Alive = 2 };
            window.SelectCrew(source.Id, crew.Group);
            receive.Invoke(window, new object[] { new WGCrewSetupResponse
                { Action = WGCrewSetupAction.List, Grids = new() { source, destination }, Crews = new() { crew } } });
            order.SelectId((int) WGPilotOrder.Dock);
            type.GetMethod("UpdateOrderFields", flags)!.Invoke(window, null);
            Assert.That(targetLine.Visible, Is.True);
            Assert.That(target.ItemCount, Is.EqualTo(2), "Placeholder and destination; own ship is excluded.");
            target.SelectId(1);
            receive.Invoke(window, new object[] { new WGCrewSetupResponse
                { Action = WGCrewSetupAction.List, Grids = new() { destination, source }, Crews = new() { crew } } });
            Assert.That(target.SelectedId, Is.EqualTo(0), "Selection survives a reordered grid refresh.");
            window.Close();
            window.Teardown();
        });
        await RunTicks(5);
    }

    /// <summary>A pilot uses real thrusters to dock without the FTL fallback.</summary>
    [Test]
    public async Task PilotDocksWithThrusters()
    {
        var (deck, target, ownDock, _) = await CreateDockingPair(gravity: true);
        EntityUid pilot = default, helm = default;
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            helm = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(3.5f, 3.5f)));
            var transform = Server.System<SharedTransformSystem>();
            for (var index = 0; index < 4; index++)
            {
                var thruster = SEntMan.SpawnAtPosition("WGTestCrewThruster", new EntityCoordinates(deck, new Vector2(1.5f + index, 5.5f)));
                transform.SetLocalRotation(thruster, Angle.FromDegrees(index * 90));
                var thrust = SEntMan.GetComponent<ThrusterComponent>(thruster);
                Server.System<ThrusterSystem>().EnableThruster(thruster, thrust);
            }
            var gyro = SEntMan.SpawnAtPosition("WGTestCrewGyroscope", new EntityCoordinates(deck, new Vector2(5.5f, 4.5f)));
            Server.System<ThrusterSystem>().EnableThruster(gyro, SEntMan.GetComponent<ThrusterComponent>(gyro));
            pilot = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Pilot,
                new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)), "flight")!.Value;
            SEntMan.GetComponent<HTNComponent>(pilot).SleepPlayerCheckRangeOverride = 1000;
            var duty = SEntMan.GetComponent<WGPilotDutyComponent>(pilot);
            duty.DockStandoff = 15;
            Server.System<WGPilotDutySystem>().Dock(pilot, target);
        });
        await WaitUntil(() => SEntMan.GetComponent<DockingComponent>(ownDock).Docked, 9000,
            () => $"Thruster docking did not complete: {DescribePilot(pilot, helm)} pos={SEntMan.GetComponent<TransformComponent>(deck).LocalPosition} velocity={SEntMan.GetComponent<PhysicsComponent>(deck).LinearVelocity}");
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<WGPilotDutyComponent>(pilot).Orders, Is.EqualTo(WGPilotOrder.Hold)));
    }
}
