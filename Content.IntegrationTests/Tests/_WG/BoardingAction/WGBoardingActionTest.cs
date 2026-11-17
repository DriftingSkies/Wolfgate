using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WG.BoardingAction;
using Content.Client._WG.Cockpit;
using Content.IntegrationTests.Tests._WG.CargoLoad;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.Projectiles.TargetGuided;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server._Crescent.ShipShields;
using Content.Server._Crescent.ShipShields.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Server._WG.BoardingAction;
using Content.Server._WG.BoardingAction.Components;
using Content.Server._WG.Tether;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared._WG.BoardingAction;
using Content.Shared._WG.Tether;
using Content.Shared._WG.Tether.Harpoon;
using Content.Shared.Projectiles;
using Content.Shared.Shuttles.Components;
using Content.Shared.Tiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WG.BoardingAction;

/// <summary>The FTL block, the hacking harpoon and what each does to both ships.</summary>
[TestFixture]
public sealed class WGBoardingActionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGTestHackGun
  parent: WGHackHarpoonGun
  components:
  - type: BallisticAmmoProvider
    proto: WGHackDart
    capacity: 1
    cycleable: false
  - type: WGHackHarpoonGun
    lockdownDelay: 0.5
    arcInterval: 0.2

# Enough of a ship gun to be asked whether it may fire.
- type: entity
  id: WGTestShipGun
  components:
  - type: Gun
  - type: FireControllable
";

    private const string Wall = "WallSolid";
    private const float ParkDamping = 2.5f;
    private const string ShipGun = "WeaponTurretL85Autocannon";

    [Test]
    public async Task HeldShipCantJumpUntilEverythingLetsGo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var elsewhere = await pair.CreateTestMap();
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var shuttles = entities.System<ShuttleSystem>();
            var disruption = entities.System<WGFtlDisruptionSystem>();
            var ship = map.Grid.Owner;
            var shuttle = entities.EnsureComponent<ShuttleComponent>(ship);
            var first = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var second = entities.SpawnEntity(null, MapCoordinates.Nullspace);

            disruption.Disrupt(ship, first);
            disruption.Disrupt(ship, second);
            Assert.That(shuttles.CanFTL(ship, out _), Is.False);

            shuttles.FTLToCoordinates(ship, shuttle, new EntityCoordinates(elsewhere.MapUid, Vector2.Zero), Angle.Zero);
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.False,
                "A jump started by anything but the console is refused too.");

            disruption.Release(ship, first);
            Assert.That(shuttles.CanFTL(ship, out _), Is.False, "One source letting go leaves the other holding.");

            disruption.Release(ship, second);
            Assert.That(shuttles.CanFTL(ship, out _), Is.True);
            Assert.That(entities.HasComponent<WGFtlDisruptedComponent>(ship), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DisruptionStopsASpoolAndTheShipCanJumpAfter()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var elsewhere = await pair.CreateTestMap();
        var entities = server.EntMan;
        var ship = map.Grid.Owner;

        await server.WaitPost(() =>
        {
            var shuttle = entities.EnsureComponent<ShuttleComponent>(ship);
            entities.System<ShuttleSystem>().FTLToCoordinates(ship, shuttle,
                new EntityCoordinates(elsewhere.MapUid, Vector2.Zero), Angle.Zero, startupTime: 30f, hyperspaceTime: 30f);
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.True);
            entities.System<WGFtlDisruptionSystem>().Disrupt(ship, ship);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.False, "The spool stops where it was.");
            Assert.That(entities.GetComponent<TransformComponent>(ship).MapUid, Is.EqualTo(map.MapUid));
            entities.System<WGFtlDisruptionSystem>().Release(ship, ship);

            var shuttle = entities.GetComponent<ShuttleComponent>(ship);
            entities.System<ShuttleSystem>().FTLToCoordinates(ship, shuttle,
                new EntityCoordinates(elsewhere.MapUid, Vector2.Zero), Angle.Zero, startupTime: 30f, hyperspaceTime: 30f);
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.True, "Nothing is left behind to stop the next jump.");
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("Thruster")]
    [TestCase(ShipGun)]
    public async Task DartSinksIntoAMachineAndGoesWithIt(string machine)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid theirs = default, harpoon = default, target = default, dart = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            var ours = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            var at = new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f));
            if (machine == ShipGun)
                entities.SpawnEntity("HardpointBallisticLight", at);

            target = entities.SpawnEntity(machine, at);
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(ours, new Vector2(2.5f, 1.5f)));
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() => Fire(entities, harpoon, new MapCoordinates(new Vector2(30.5f, 1.5f), map.MapId)));
        await server.WaitRunTicks(15);

        await server.WaitAssertion(() =>
        {
            var gun = entities.GetComponent<WGHackHarpoonGunComponent>(harpoon);
            Assert.That(gun.Target, Is.EqualTo(theirs), "A square-on shot latches into a machine as it does a wall.");
            dart = gun.Dart!.Value;
            Assert.That(entities.GetComponent<TransformComponent>(dart).ParentUid, Is.EqualTo(target));
            entities.DeleteEntity(target);
        });

        await server.WaitRunTicks(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Cable, Is.Null);
            Assert.That(entities.HasComponent<WGHackedGridComponent>(theirs), Is.False, "The hack goes with what held the dart.");
            Assert.That(entities.System<ShuttleSystem>().CanFTL(theirs, out _), Is.True);
            Assert.That(entities.Deleted(dart), Is.True, "So does the dart.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HackLandsInStagesAndEndsWithTheCable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid ours = default, theirs = default, harpoon = default, ourGun = default, theirGun = default, thruster = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            ours = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            entities.SpawnEntity(Wall, new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f)));
            thruster = entities.SpawnEntity("Thruster", new EntityCoordinates(theirs, new Vector2(2.5f, 2.5f)));
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(ours, new Vector2(2.5f, 1.5f)));
            ourGun = entities.SpawnEntity("WGTestShipGun", new EntityCoordinates(ours, new Vector2(0.5f, 0.5f)));
            theirGun = entities.SpawnEntity("WGTestShipGun", new EntityCoordinates(theirs, new Vector2(2.5f, 0.5f)));
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() => Fire(entities, harpoon, new MapCoordinates(new Vector2(30.5f, 1.5f), map.MapId)));
        await server.WaitRunTicks(15);

        var atThem = new MapCoordinates(new Vector2(31f, 1f), map.MapId);
        var atUs = new MapCoordinates(new Vector2(1.5f, 1.5f), map.MapId);
        var awayFromUs = new MapCoordinates(new Vector2(60f, 30f), map.MapId);

        await server.WaitAssertion(() =>
        {
            var gun = entities.GetComponent<WGHackHarpoonGunComponent>(harpoon);
            Assert.That(gun.Target, Is.EqualTo(theirs), "A square-on shot latches.");
            Assert.That(entities.System<ShuttleSystem>().CanFTL(theirs, out _), Is.False, "FTL drops on the latch.");
            Assert.That(entities.GetComponent<RopeComponent>(gun.Cable!.Value).JointId, Is.Null, "The cable pulls on nothing.");

            Assert.That(Refused(entities, ourGun, atThem), Is.True, "None of the hacker's guns can fire.");
            Assert.That(Refused(entities, theirGun, atUs), Is.False, "The lockdown hasn't landed yet.");
            Assert.That(entities.System<ShuttleSystem>().NfGetInertiaDampeningMode(theirs),
                Is.Not.EqualTo(InertiaDampeningMode.Anchor));

            var helm = entities.System<WGBoardingHelmSystem>();
            Assert.That(helm.GetState(theirs)?.Hackers, Has.Length.EqualTo(1));
            Assert.That(helm.GetState(theirs)?.Lockdown, Is.False);
            Assert.That(helm.GetState(ours)?.CableTarget, Is.Not.Null);
        });

        await server.WaitRunTicks(40);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<ShuttleSystem>().NfGetInertiaDampeningMode(theirs),
                Is.EqualTo(InertiaDampeningMode.Anchor), "The lockdown parks the ship.");
            Assert.That(Refused(entities, theirGun, atUs), Is.True, "Its guns can't be laid on the hacker.");
            Assert.That(Refused(entities, theirGun, awayFromUs), Is.False, "They still bear on anything else.");
            Assert.That(entities.System<WGBoardingHelmSystem>().GetState(theirs)?.Lockdown, Is.True);

            Assert.That(Arcs(entities, thruster), Is.EqualTo(1), "Each engine carries one arc for as long as the hack runs.");

            var shuttle = entities.GetComponent<ShuttleComponent>(theirs);
            var body = entities.GetComponent<PhysicsComponent>(theirs);
            var xform = entities.GetComponent<TransformComponent>(theirs);
            Assert.That(shuttle.BodyModifier, Is.GreaterThan(ParkDamping), "The lockdown holds it harder than park.");
            Assert.That(entities.System<ShuttleSystem>().SetInertiaDampening(theirs, body, shuttle, xform, InertiaDampeningMode.Off),
                Is.False, "Steering turns cruise on every update, and must never get through.");
            Assert.That(shuttle.BodyModifier, Is.GreaterThan(ParkDamping));
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<ShuttleSystem>().NfGetInertiaDampeningMode(theirs),
                Is.EqualTo(InertiaDampeningMode.Anchor), "Neither a pilot nor an NPC can leave park.");

            entities.System<WGHackHarpoonSystem>().ReleaseCables(ours);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<ShuttleSystem>().CanFTL(theirs, out _), Is.True);

            var shuttle = entities.GetComponent<ShuttleComponent>(theirs);
            Assert.That(shuttle.BodyModifier, Is.EqualTo(ParkDamping), "It is left in park.");
            Assert.That(entities.System<ShuttleSystem>().SetInertiaDampening(theirs, entities.GetComponent<PhysicsComponent>(theirs),
                shuttle, entities.GetComponent<TransformComponent>(theirs), InertiaDampeningMode.Dampen), Is.True, "The pilot has the helm back.");

            Assert.That(Refused(entities, ourGun, atThem), Is.False, "The hacker's guns are back.");
            Assert.That(Refused(entities, theirGun, atUs), Is.False, "So is everyone else's aim.");
            Assert.That(entities.HasComponent<WGHackedGridComponent>(theirs), Is.False);
            Assert.That(entities.HasComponent<WGHackSourceGridComponent>(ours), Is.False);
            Assert.That(Arcs(entities, thruster), Is.Zero, "The arcs go with the hack.");
            Assert.That(entities.System<WGBoardingHelmSystem>().GetState(ours), Is.Null);
            Assert.That(entities.System<WGBoardingHelmSystem>().GetState(theirs), Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FiresThroughFireControlAndTurnsOntoItsTarget()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid theirs = default, harpoon = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            var ours = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            entities.SpawnEntity(Wall, new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f)));
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(ours, new Vector2(2.5f, 1.5f)));
        });

        await server.WaitRunTicks(5);

        // The way gunnery does it: asked again and again while the gun comes round.
        var fireControl = entities.System<FireControlSystem>();
        var aim = new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f));
        for (var i = 0; i < 180; i++)
        {
            await server.WaitPost(() => fireControl.AttemptFire(harpoon, harpoon, aim, noServer: true));
            await server.WaitRunTicks(1);
            if (entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Target != null)
                break;
        }

        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Target, Is.EqualTo(theirs)));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CablePaysOutThenPartsPastItsLength()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();
        var transforms = entities.System<SharedTransformSystem>();

        EntityUid ours = default, theirs = default, harpoon = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            ours = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            entities.SpawnEntity(Wall, new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f)));
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(ours, new Vector2(2.5f, 1.5f)));
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() => Fire(entities, harpoon, new MapCoordinates(new Vector2(30.5f, 1.5f), map.MapId)));
        await server.WaitRunTicks(15);

        // Eased out rather than teleported, so the cable sees the same steps a flying ship would give it.
        for (var distance = 35f; distance <= 240f; distance += 5f)
        {
            await server.WaitPost(() => transforms.SetWorldPosition(theirs, new Vector2(distance, 0f)));
            await server.WaitRunTicks(2);
        }

        await server.WaitAssertion(() =>
        {
            var gun = entities.GetComponent<WGHackHarpoonGunComponent>(harpoon);
            Assert.That(gun.Target, Is.EqualTo(theirs), "The cable pays out with the ships.");
            Assert.That(entities.GetComponent<RopeComponent>(gun.Cable!.Value).Length, Is.GreaterThan(230f));
            transforms.SetWorldPosition(theirs, new Vector2(300f, 0f));
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Target, Is.Null, "It parts past its length.");
            Assert.That(entities.System<ShuttleSystem>().CanFTL(theirs, out _), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShieldStopsTheDart()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid ours = default, theirs = default, harpoon = default, dart = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            ours = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            entities.SpawnEntity(Wall, new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f)));
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(ours, new Vector2(2.5f, 1.5f)));
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", new EntityCoordinates(theirs, new Vector2(2.5f, 2.5f)));
            entities.GetComponent<ApcPowerReceiverComponent>(generator).NeedsPower = false;
        });

        await server.WaitRunTicks(100);
        await server.WaitPost(() =>
        {
            Assert.That(entities.HasComponent<ShipShieldedComponent>(theirs), Is.True, "The test needs a field up.");
            Fire(entities, harpoon, new MapCoordinates(new Vector2(30.5f, 1.5f), map.MapId));
            dart = entities.GetEntity(entities.GetComponent<ShipHarpoonTurretComponent>(harpoon).Harpoon!.Value);
        });

        await server.WaitRunTicks(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(!entities.EntityExists(dart) || entities.IsQueuedForDeletion(dart), Is.True, "The dart is lost on the field.");
            Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Target, Is.Null);
            Assert.That(entities.GetComponent<RopeAttachPointComponent>(harpoon).Ropes, Is.Empty);
            Assert.That(entities.HasComponent<WGHackedGridComponent>(theirs), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DartLeavesItsOwnShipsShield()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid ours = default, theirs = default, harpoon = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            ours = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            entities.SpawnEntity(Wall, new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f)));
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(ours, new Vector2(2.5f, 1.5f)));
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", new EntityCoordinates(ours, new Vector2(0.5f, 0.5f)));
            entities.GetComponent<ApcPowerReceiverComponent>(generator).NeedsPower = false;
        });

        await server.WaitRunTicks(100);
        await server.WaitPost(() =>
        {
            Assert.That(entities.HasComponent<ShipShieldedComponent>(ours), Is.True, "The test needs a field up.");
            Fire(entities, harpoon, new MapCoordinates(new Vector2(30.5f, 1.5f), map.MapId));
        });

        await server.WaitRunTicks(30);
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Target, Is.EqualTo(theirs),
                "The firing ship's own shield lets its dart out."));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SwitchedOffShieldLetsTheDartThrough()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid ours = default, theirs = default, harpoon = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            ours = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            entities.SpawnEntity(Wall, new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f)));
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(ours, new Vector2(2.5f, 1.5f)));
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", new EntityCoordinates(theirs, new Vector2(2.5f, 2.5f)));
            entities.GetComponent<ApcPowerReceiverComponent>(generator).NeedsPower = false;
        });

        await server.WaitRunTicks(100);
        await server.WaitPost(() =>
        {
            Assert.That(entities.HasComponent<ShipShieldedComponent>(theirs), Is.True, "The test needs a field to switch off.");
            Assert.That(entities.System<ShipShieldsSystem>().SetWolfgateShieldEnabled(theirs, false), Is.True);
        });

        await server.WaitRunTicks(100);
        await server.WaitPost(() =>
        {
            Assert.That(entities.HasComponent<ShipShieldedComponent>(theirs), Is.False, "The field drops when switched off.");
            Fire(entities, harpoon, new MapCoordinates(new Vector2(30.5f, 1.5f), map.MapId));
        });

        await server.WaitRunTicks(30);
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Target, Is.EqualTo(theirs),
                "A shield that is off stops nothing."));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShotOffAProtectedGridLeavesNoHack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid ours = default, theirs = default, harpoon = default, ourGun = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            ours = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            entities.EnsureComponent<ProtectedGridComponent>(theirs);
            entities.SpawnEntity(Wall, new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f)));
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(ours, new Vector2(2.5f, 1.5f)));
            ourGun = entities.SpawnEntity("WGTestShipGun", new EntityCoordinates(ours, new Vector2(0.5f, 0.5f)));
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() => Fire(entities, harpoon, new MapCoordinates(new Vector2(30.5f, 1.5f), map.MapId)));
        await server.WaitRunTicks(20);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Cable, Is.Null, "The cable comes straight off.");
            Assert.That(entities.HasComponent<WGHackedGridComponent>(theirs), Is.False);
            Assert.That(Refused(entities, ourGun, new MapCoordinates(new Vector2(31f, 1f), map.MapId)), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HelmSurvivesTheHolderBeingDeleted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var holder = MakeShip(entities, maps, map.MapId, new Vector2(100f, 0f));
            entities.EnsureComponent<WGHackedGridComponent>(map.Grid.Owner).Hackers[holder] = holder;
            entities.DeleteEntity(holder);

            Assert.That(entities.System<WGBoardingHelmSystem>().GetState(map.Grid.Owner), Is.Null,
                "A ship that is gone isn't named, and must not throw while a helm refreshes.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BannerShowsEachRowOnlyWhenItApplies()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        await pair.Client.WaitAssertion(() =>
        {
            var banner = new WGBoardingBanner();
            banner.SetState(null);
            Assert.That(banner.Visible, Is.False);

            banner.SetState(new WGBoardingHelmState { Hackers = new[] { "Raider" } });
            Assert.That(banner.Visible, Is.True);

            banner.SetState(new WGBoardingHelmState());
            Assert.That(banner.Visible, Is.False);

            // The console theme clips every button's text, as it does in the cockpit.
            banner.SetState(new WGBoardingHelmState { CableTarget = "Raider" });
            var release = WGCockpitLease.Descendants(banner).OfType<Button>().Single();
            release.ClipText = true;
            banner.Measure(new Vector2(1700f, 100f));
            Assert.That(release.DesiredSize.X, Is.GreaterThanOrEqualTo(WGBoardingBanner.ReleaseWidth),
                "The release button keeps its width when its text is clipped.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HeldShipHoldsItsDockedGroup()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var elsewhere = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid held = default, partner = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            held = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            partner = MakeShip(entities, maps, map.MapId, new Vector2(0f, 3f));
            CargoDeck.Dock(entities, CargoDeck.AddPort(entities, held, new Vector2(0.5f, 2.5f), 180f),
                CargoDeck.AddPort(entities, partner, new Vector2(0.5f, 0.5f), 0f));
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var disruption = entities.System<WGFtlDisruptionSystem>();
            var shuttles = entities.System<ShuttleSystem>();
            var shuttle = entities.GetComponent<ShuttleComponent>(partner);
            shuttles.FTLToCoordinates(partner, shuttle, new EntityCoordinates(elsewhere.MapUid, Vector2.Zero), Angle.Zero,
                startupTime: 30f, hyperspaceTime: 30f);
            Assert.That(entities.HasComponent<FTLComponent>(partner), Is.True, "Nothing holds the group yet.");

            disruption.Disrupt(held, held);
            Assert.That(disruption.IsGroupDisrupted(partner), Is.True);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<FTLComponent>(partner), Is.False, "The group's spool stops with one ship held.");

            var shuttle = entities.GetComponent<ShuttleComponent>(partner);
            entities.System<ShuttleSystem>().FTLToCoordinates(partner, shuttle,
                new EntityCoordinates(elsewhere.MapUid, Vector2.Zero), Angle.Zero, startupTime: 30f, hyperspaceTime: 30f);
            Assert.That(entities.HasComponent<FTLComponent>(partner), Is.False, "Nor can a new jump start.");
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ShipDockedToAProtectedGridCantBeHeld(bool dockedFirst)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid theirs = default, station = default, harpoon = default;
        void DockThem() => CargoDeck.Dock(entities, CargoDeck.AddPort(entities, theirs, new Vector2(2.5f, 1.5f), 90f),
            CargoDeck.AddPort(entities, station, new Vector2(0.5f, 1.5f), -90f));

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            var ours = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            station = MakeShip(entities, maps, map.MapId, new Vector2(33f, 0f));
            entities.EnsureComponent<ProtectedGridComponent>(station);
            entities.SpawnEntity(Wall, new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f)));
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(ours, new Vector2(2.5f, 1.5f)));
            if (dockedFirst)
                DockThem();
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() => Fire(entities, harpoon, new MapCoordinates(new Vector2(30.5f, 1.5f), map.MapId)));
        await server.WaitRunTicks(15);

        if (!dockedFirst)
        {
            await server.WaitPost(() =>
            {
                Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Target, Is.EqualTo(theirs), "A free ship latches.");
                DockThem();
            });

            await server.WaitRunTicks(3);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Cable, Is.Null, "The cable comes off.");
            Assert.That(entities.HasComponent<WGHackedGridComponent>(theirs), Is.False);
            Assert.That(entities.System<WGFtlDisruptionSystem>().IsGroupDisrupted(theirs), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DockedShipsShareTheGunLock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid pod = default, carrier = default, theirs = default, harpoon = default, carrierGun = default, gunner = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            pod = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            carrier = MakeShip(entities, maps, map.MapId, new Vector2(0f, 3f));
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            CargoDeck.Dock(entities, CargoDeck.AddPort(entities, pod, new Vector2(0.5f, 2.5f), 180f),
                CargoDeck.AddPort(entities, carrier, new Vector2(0.5f, 0.5f), 0f));
            entities.SpawnEntity(Wall, new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f)));
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(pod, new Vector2(2.5f, 1.5f)));
            carrierGun = entities.SpawnEntity("WGTestShipGun", new EntityCoordinates(carrier, new Vector2(1.5f, 1.5f)));
            gunner = entities.SpawnEntity(null, new EntityCoordinates(carrier, new Vector2(2.5f, 2.5f)));
        });

        await server.WaitRunTicks(5);
        var atThem = new MapCoordinates(new Vector2(31f, 1f), map.MapId);
        await server.WaitAssertion(() =>
            Assert.That(Refused(entities, carrierGun, atThem), Is.False, "Nothing locks the carrier's guns yet."));

        await server.WaitPost(() => Fire(entities, harpoon, new MapCoordinates(new Vector2(30.5f, 1.5f), map.MapId)));
        await server.WaitRunTicks(15);

        await server.WaitAssertion(() =>
        {
            var hack = entities.System<WGHackHarpoonSystem>();
            Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Target, Is.EqualTo(theirs));
            Assert.That(Refused(entities, carrierGun, atThem), Is.True, "A docked ship can't fire past the penalty.");
            Assert.That(entities.System<WGBoardingHelmSystem>().GetState(carrier)?.CableTarget, Is.Not.Null,
                "Its helm says why, and carries the release button.");

            var weapons = new List<NetEntity> { entities.GetNetEntity(carrierGun) };
            var aim = entities.GetNetCoordinates(entities.System<SharedTransformSystem>().ToCoordinates(atThem));
            Assert.That(hack.WarnGunner(carrierGun, gunner, weapons, aim), Is.True, "The gunner is told.");
            Assert.That(hack.WarnGunner(carrierGun, gunner, weapons, aim), Is.False, "Once, not on every shot held down.");

            hack.ReleaseCables(carrier);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Cable, Is.Null);
            Assert.That(Refused(entities, carrierGun, atThem), Is.False, "Its guns are back.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StationDoesntPassTheGunLockOn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid theirs = default, neighbour = default, harpoon = default, neighbourGun = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            var ours = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            var station = MakeShip(entities, maps, map.MapId, new Vector2(0f, 3f));
            neighbour = MakeShip(entities, maps, map.MapId, new Vector2(0f, 6f));
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            entities.EnsureComponent<ProtectedGridComponent>(station);
            CargoDeck.Dock(entities, CargoDeck.AddPort(entities, ours, new Vector2(0.5f, 2.5f), 180f),
                CargoDeck.AddPort(entities, station, new Vector2(0.5f, 0.5f), 0f));
            CargoDeck.Dock(entities, CargoDeck.AddPort(entities, station, new Vector2(0.5f, 2.5f), 180f),
                CargoDeck.AddPort(entities, neighbour, new Vector2(0.5f, 0.5f), 0f));
            entities.SpawnEntity(Wall, new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f)));
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(ours, new Vector2(2.5f, 1.5f)));
            neighbourGun = entities.SpawnEntity("WGTestShipGun", new EntityCoordinates(neighbour, new Vector2(1.5f, 1.5f)));
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() => Fire(entities, harpoon, new MapCoordinates(new Vector2(30.5f, 1.5f), map.MapId)));
        await server.WaitRunTicks(15);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WGHackHarpoonGunComponent>(harpoon).Target, Is.EqualTo(theirs));
            Assert.That(Refused(entities, neighbourGun, new MapCoordinates(new Vector2(31f, 1f), map.MapId)), Is.False,
                "A ship beside the hacker at a station keeps its guns.");
            Assert.That(entities.System<WGBoardingHelmSystem>().GetState(neighbour), Is.Null, "And gets no release button.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LockdownTurnsMissilesOffTheHacker()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();

        EntityUid ours = default, theirs = default, other = default, harpoon = default, seeker = default, missile = default;
        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            ours = MakeShip(entities, maps, map.MapId, Vector2.Zero);
            theirs = MakeShip(entities, maps, map.MapId, new Vector2(30f, 0f));
            other = MakeShip(entities, maps, map.MapId, new Vector2(15f, 40f));
            entities.EnsureComponent<TargetSeekingTargetComponent>(ours);
            entities.EnsureComponent<TargetSeekingTargetComponent>(other);
            entities.SpawnEntity(Wall, new EntityCoordinates(theirs, new Vector2(0.5f, 1.5f)));
            harpoon = entities.SpawnEntity("WGTestHackGun", new EntityCoordinates(ours, new Vector2(2.5f, 1.5f)));
            var launcher = entities.SpawnEntity("WGTestShipGun", new EntityCoordinates(theirs, new Vector2(2.5f, 0.5f)));

            var projectiles = entities.System<SharedProjectileSystem>();
            var at = new MapCoordinates(new Vector2(15f, 1.5f), map.MapId);
            seeker = entities.SpawnEntity(null, at);
            projectiles.SetShooter(seeker, entities.EnsureComponent<ProjectileComponent>(seeker), launcher);
            var seeking = entities.EnsureComponent<TargetSeekingComponent>(seeker);
            seeking.ScanArc = 360f;
            seeking.DetectionRange = 1000f;
            missile = entities.SpawnEntity(null, at);
            projectiles.SetShooter(missile, entities.EnsureComponent<ProjectileComponent>(missile), launcher);
            entities.EnsureComponent<PhysicsComponent>(missile);
            // Nothing here moves a cursor, which otherwise cuts the missile's control after a second.
            entities.EnsureComponent<TargetGuidedComponent>(missile).FallbackTime = 1000f;
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() => Fire(entities, harpoon, new MapCoordinates(new Vector2(30.5f, 1.5f), map.MapId)));
        await server.WaitRunTicks(15);

        var transform = entities.System<SharedTransformSystem>();
        var atUs = new MapCoordinates(new Vector2(1.5f, 1.5f), map.MapId);
        var awayFromUs = new MapCoordinates(new Vector2(60f, 30f), map.MapId);
        await server.WaitAssertion(() =>
        {
            var seeking = entities.GetComponent<TargetSeekingComponent>(seeker);
            var seekers = entities.System<TargetSeekingSystem>();
            seekers.AcquireTarget(seeker, seeking, entities.GetComponent<TransformComponent>(seeker));
            Assert.That(seeking.CurrentTarget, Is.EqualTo(ours), "Until the lockdown lands its seekers still take the closest ship.");

            var guided = entities.GetComponent<TargetGuidedComponent>(missile);
            entities.System<TargetGuidedSystem>().SetTargetPosition(missile, transform.ToCoordinates(atUs), guided);
            Assert.That(guided.TargetPosition, Is.Not.Null, "So can its missiles be steered.");
        });

        await server.WaitRunTicks(40);
        await server.WaitAssertion(() =>
        {
            var seeking = entities.GetComponent<TargetSeekingComponent>(seeker);
            Assert.That(seeking.CurrentTarget, Is.Null, "A seeker on the hacker lets go when the lockdown lands.");

            entities.System<TargetSeekingSystem>().AcquireTarget(seeker, seeking, entities.GetComponent<TransformComponent>(seeker));
            Assert.That(seeking.CurrentTarget, Is.EqualTo(other), "It can't take the hacker again, but anything else is fair.");

            var guided = entities.GetComponent<TargetGuidedComponent>(missile);
            Assert.That(guided.TargetPosition, Is.Null, "A missile aimed at the hacker loses its aim.");

            var guidance = entities.System<TargetGuidedSystem>();
            guidance.SetTargetPosition(missile, transform.ToCoordinates(atUs), guided);
            Assert.That(guided.TargetPosition, Is.Null, "It can't be steered back.");
            guidance.SetTargetPosition(missile, transform.ToCoordinates(awayFromUs), guided);
            Assert.That(guided.TargetPosition, Is.Not.Null, "It still steers anywhere else.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>How many arc effects are running on an engine.</summary>
    private static int Arcs(IEntityManager entities, EntityUid engine)
    {
        var arcs = 0;
        var effects = entities.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (effects.MoveNext(out var uid, out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID == "WGEffectHackArc" && xform.ParentUid == engine && !entities.IsQueuedForDeletion(uid))
                arcs++;
        }

        return arcs;
    }

    /// <summary>Whether a ship gun is refused a shot at this point.</summary>
    private static bool Refused(IEntityManager entities, EntityUid gun, MapCoordinates aim)
    {
        var comp = entities.GetComponent<GunComponent>(gun);
        comp.ShootCoordinates = entities.System<SharedTransformSystem>().ToCoordinates(aim);
        var ev = new ShotAttemptedEvent { User = gun, Used = (gun, comp) };
        entities.EventBus.RaiseLocalEvent(gun, ref ev);
        return ev.Cancelled;
    }

    /// <summary>Fires a hacking gun the way fire control does, as its own user.</summary>
    private static void Fire(IEntityManager entities, EntityUid gun, MapCoordinates target)
    {
        var comp = entities.GetComponent<GunComponent>(gun);
        var coordinates = entities.System<SharedTransformSystem>().ToCoordinates(target);
        entities.System<SharedGunSystem>().AttemptShoot(gun, gun, comp, coordinates);
        Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(gun).Harpoon, Is.Not.Null, "The gun must have fired.");
    }

    /// <summary>A free-flying three by three ship.</summary>
    private static EntityUid MakeShip(IEntityManager entities, SharedMapSystem maps, MapId map, Vector2 position)
    {
        var grid = maps.CreateGridEntity(map);
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
        for (var x = 0; x < 3; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        maps.SetTiles(grid.Owner, grid.Comp, tiles);
        entities.System<SharedPhysicsSystem>().SetBodyType(grid.Owner, BodyType.Dynamic);
        entities.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, position);
        entities.EnsureComponent<ShuttleComponent>(grid.Owner);
        return grid.Owner;
    }
}
