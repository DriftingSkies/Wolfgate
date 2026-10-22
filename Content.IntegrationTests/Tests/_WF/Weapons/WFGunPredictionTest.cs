#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._RMC14.Weapons.Ranged.Prediction;
using Content.Shared.CombatMode;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using ClientGunSystem = Content.Client.Weapons.Ranged.Systems.GunSystem;
using ServerPrediction = Content.Server._RMC14.Weapons.Ranged.Prediction.GunPredictionSystem;

namespace Content.IntegrationTests.Tests._WF.Weapons;

/// <summary>
/// Throwaway checks for client-predicted projectiles.
/// </summary>
public sealed class WFGunPredictionTest : InteractionTest
{
    protected override string PlayerPrototype => "WFPredTestMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFPredTestMob
  parent: InteractionTestMob
  components:
  - type: CombatMode

- type: entity
  id: WFPredTestBullet
  parent: BaseBullet

- type: entity
  id: WFPredTestPellets
  parent: BaseBullet
  components:
  - type: ProjectileSpread
    proto: WFPredTestBullet
    count: 4
    spread: 20

- type: entity
  id: WFPredTestCartridge
  components:
  - type: CartridgeAmmo
    proto: WFPredTestBullet
    deleteOnSpawn: true

- type: entity
  id: WFPredTestShell
  components:
  - type: CartridgeAmmo
    proto: WFPredTestPellets
    deleteOnSpawn: true

- type: entity
  id: WFPredTestGun
  components:
  - type: Item
  - type: Gun
    fireRate: 1
    minAngle: 20
    maxAngle: 40
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    proto: WFPredTestCartridge
    capacity: 10

- type: entity
  id: WFPredTestShotgun
  parent: WFPredTestGun
  components:
  - type: BallisticAmmoProvider
    proto: WFPredTestShell
    capacity: 10

- type: entity
  id: WFPredTestLaser
  components:
  - type: Item
  - type: Gun
    fireRate: 1
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: HitscanBatteryAmmoProvider
    proto: RedLightLaser
    fireCost: 20
  - type: Battery
    maxCharge: 1000
    startingCharge: 1000

- type: entity
  id: WFPredTestPulseSniper
  parent: WFPredTestLaser
  components:
  - type: HitscanBatteryAmmoProvider
    proto: UllmanPulseHeavy

- type: entity
  id: WFPredTestHeavyLaser
  parent: WFPredTestLaser
  components:
  - type: HitscanBatteryAmmoProvider
    proto: RedHeavyLaser

- type: entity
  id: WFPredTestDiffractBeam
  parent: RedLightLaser
  components:
  - type: HitscanDiffract
    beamCount: 3
    diffractedBeamPrototype: RedLightLaser

- type: entity
  id: WFPredTestDiffractLaser
  parent: WFPredTestLaser
  components:
  - type: HitscanBatteryAmmoProvider
    proto: WFPredTestDiffractBeam

- type: entity
  id: WFPredTestHitscanCartridge
  components:
  - type: CartridgeAmmo
    proto: Magnum45
    deleteOnSpawn: true

- type: entity
  id: WFPredTestHitscanRevolver
  parent: WFPredTestGun
  components:
  - type: BallisticAmmoProvider
    proto: WFPredTestHitscanCartridge
    capacity: 10
";

    private sealed record ServerCopy(EntityUid Uid, int ClientId, EntityUid? ClientEnt, bool ShooterMatches, Vector2 Velocity);

    private async Task<EntityUid> ArmPlayer(string gun)
    {
        EntityUid uid = default;
        var picked = false;
        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(SPlayer, true);
            uid = SEntMan.SpawnEntity(gun, SEntMan.GetCoordinates(PlayerCoords));
            picked = HandSys.TryPickupAnyHand(SPlayer, uid);
        });
        Assert.That(picked, "gun not picked up");

        // Picking a gun up puts it on a full fire-interval cooldown.
        await RunTicks(40);
        return uid;
    }

    /// <summary>
    /// Fires on the client the way GunSystem.Update does, then sends the request to the server.
    /// </summary>
    private async Task<(List<int>? Slots, uint Tick)> ClientShoot(NetEntity gun, NetCoordinates aim)
    {
        List<int>? slots = null;
        uint tick = 0;
        await Client.WaitPost(() =>
        {
            tick = CTiming.CurTick.Value;

            // Mirrors what GunSystem.Update does when the player pulls the trigger.
            var gunSystem = CEntMan.System<ClientGunSystem>();
            slots = gunSystem.ShootRequested(gun, aim, null, null, ClientSession, true);
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new RequestShootEvent
            {
                Gun = gun,
                Coordinates = aim,
                Shot = slots,
                Predicted = gunSystem.DrewHitscan,
            });
        });
        return (slots, tick);
    }

    private async Task<List<ServerCopy>> ServerCopies()
    {
        var copies = new List<ServerCopy>();
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<PredictedProjectileServerComponent, PhysicsComponent>();
            while (query.MoveNext(out var uid, out var comp, out var physics))
            {
                copies.Add(new ServerCopy(uid, comp.ClientId, comp.ClientEnt, comp.Shooter == ServerSession, physics.LinearVelocity));
            }
        });
        return copies;
    }

    [Test]
    public async Task PredictedShotIsLinkedAndHidden()
    {
        var gun = SEntMan.GetNetEntity(await ArmPlayer("WFPredTestGun"));
        var aim = new NetCoordinates(Player, new Vector2(5f, 0f));

        var (slots, clientTick) = await ClientShoot(gun, aim);
        Assert.That(slots, Is.Not.Null, "client predicted nothing");
        Assert.That(slots, Has.Count.EqualTo(1));

        var clientCopy = new EntityUid(slots![0]);
        var clientSide = false;
        var tagged = false;
        var clientVelocity = Vector2.Zero;
        await Client.WaitPost(() =>
        {
            clientSide = CEntMan.IsClientSide(clientCopy);
            tagged = CEntMan.HasComponent<PredictedProjectileClientComponent>(clientCopy);
            clientVelocity = CEntMan.GetComponent<PhysicsComponent>(clientCopy).LinearVelocity;
        });
        Assert.Multiple(() =>
        {
            Assert.That(clientSide, "copy is not client-side");
            Assert.That(tagged, "copy is not tagged as predicted");
            Assert.That(clientVelocity.Length(), Is.GreaterThan(1f), "copy is not moving");
        });

        await RunTicks(10);

        var copies = await ServerCopies();
        Assert.That(copies, Has.Count.EqualTo(1), "server did not mark exactly one projectile");
        var copy = copies[0];

        var registered = false;
        await Server.WaitPost(() =>
        {
            var system = SEntMan.System<ServerPrediction>();
            var field = typeof(ServerPrediction).GetField("_predicted", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var map = (Dictionary<(Guid, int), EntityUid>) field.GetValue(system)!;
            registered = map.TryGetValue((ServerSession.UserId.UserId, slots[0]), out var linked) && linked == copy.Uid;
        });

        var angleDiff = Math.Abs(Angle.ShortestDistance(new Angle(clientVelocity), new Angle(copy.Velocity)).Theta);
        TestContext.Out.WriteLine($"client tick {clientTick}, client dir {new Angle(clientVelocity).Degrees:F3}, server dir {new Angle(copy.Velocity).Degrees:F3}");

        Assert.Multiple(() =>
        {
            Assert.That(copy.ClientId, Is.EqualTo(slots[0]), "ClientId is not the client's copy");
            Assert.That(copy.ClientEnt, Is.EqualTo(SPlayer), "ClientEnt is not the shooter");
            Assert.That(copy.ShooterMatches, "Shooter session not set");
            Assert.That(registered, "server did not register the copy for hit reports");
            Assert.That(angleDiff, Is.LessThan(0.001), "client and server spread differ");
        });

        var cServerCopy = ToClient(SEntMan.GetNetEntity(copy.Uid));
        var visible = true;
        var spent = false;
        var copyAlive = false;
        await Client.WaitPost(() =>
        {
            visible = CEntMan.GetComponent<SpriteComponent>(cServerCopy).Visible;
            spent = CEntMan.GetComponent<ProjectileComponent>(cServerCopy).ProjectileSpent;
            copyAlive = CEntMan.EntityExists(clientCopy);
        });
        Assert.Multiple(() =>
        {
            Assert.That(visible, Is.False, "server copy is visible to the shooter");
            Assert.That(spent, "server copy still collides locally");
            Assert.That(copyAlive, "predicted copy vanished early");
        });

        // Deleting the server copy drops the predicted copy that never hit.
        await Server.WaitPost(() => SEntMan.DeleteEntity(copy.Uid));
        await RunTicks(5);
        await Client.WaitPost(() => copyAlive = CEntMan.EntityExists(clientCopy));
        Assert.That(copyAlive, Is.False, "orphaned predicted copy was not removed");
    }

    /// <summary>
    /// A round the client predicted but the server didn't fire (here the server's gun is still cooling down, as when
    /// a late request finds the server already fired that round) is dropped from the shooter's screen.
    /// </summary>
    [Test]
    public async Task RefusedPredictionIsDropped()
    {
        var gunUid = await ArmPlayer("WFPredTestGun");
        var gun = SEntMan.GetNetEntity(gunUid);
        var aim = new NetCoordinates(Player, new Vector2(5f, 0f));

        await Server.WaitPost(() =>
        {
            var comp = SEntMan.GetComponent<GunComponent>(gunUid);
            comp.NextFire = STiming.CurTime + TimeSpan.FromSeconds(10);
        });

        var (slots, _) = await ClientShoot(gun, aim);
        Assert.That(slots, Is.Not.Null, "client predicted nothing");
        var clientCopy = new EntityUid(slots![0]);

        await RunTicks(10);

        var exists = true;
        await Client.WaitPost(() => exists = CEntMan.EntityExists(clientCopy));
        var copies = await ServerCopies();
        Assert.Multiple(() =>
        {
            Assert.That(copies, Is.Empty, "the server fired while cooling down");
            Assert.That(exists, Is.False, "a round the server never fired is still flying on the shooter's screen");
        });
    }

    [Test]
    public async Task PelletsLinkInOrder()
    {
        var gun = SEntMan.GetNetEntity(await ArmPlayer("WFPredTestShotgun"));
        var aim = new NetCoordinates(Player, new Vector2(5f, 0f));

        var (slots, _) = await ClientShoot(gun, aim);
        Assert.That(slots, Is.Not.Null, "client predicted nothing");
        Assert.That(slots, Has.Count.EqualTo(4));
        Assert.That(slots!.Distinct().Count(), Is.EqualTo(4));
        Assert.That(slots, Has.None.EqualTo(0));

        var clientDirections = new Dictionary<int, Angle>();
        await Client.WaitPost(() =>
        {
            foreach (var id in slots)
            {
                clientDirections[id] = new Angle(CEntMan.GetComponent<PhysicsComponent>(new EntityUid(id)).LinearVelocity);
            }
        });

        await RunTicks(10);
        var copies = await ServerCopies();
        Assert.That(copies.Select(c => c.ClientId), Is.EquivalentTo(slots));

        Assert.Multiple(() =>
        {
            foreach (var copy in copies)
            {
                var diff = Math.Abs(Angle.ShortestDistance(clientDirections[copy.ClientId], new Angle(copy.Velocity)).Theta);
                Assert.That(diff, Is.LessThan(0.001), $"pellet {copy.ClientId} flies a different way");
            }
        });

        // Clear the pellets so teardown isn't left with projectiles in flight.
        await Server.WaitPost(() =>
        {
            foreach (var copy in copies)
            {
                SEntMan.DeleteEntity(copy.Uid);
            }
        });
        await RunTicks(5);
    }

    /// <summary>
    /// Counts the beam effect entities the client is currently drawing.
    /// </summary>
    private async Task<int> ClientBeams()
    {
        var beams = 0;
        await Client.WaitPost(() =>
        {
            var query = CEntMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == "HitscanEffect")
                    beams++;
            }
        });
        return beams;
    }

    [TestCase("WFPredTestLaser")]
    [TestCase("WFPredTestPulseSniper")] // 360-tile beam
    [TestCase("WFPredTestHeavyLaser")] // jumps between mobs
    [TestCase("WFPredTestHitscanRevolver")] // hitscan cartridge that pierces
    public async Task PredictedHitscanDrawsOneBeam(string gunId)
    {
        var gun = SEntMan.GetNetEntity(await ArmPlayer(gunId));
        var aim = new NetCoordinates(Player, new Vector2(5f, 0f));

        await ClientShoot(gun, aim);
        var drawn = await ClientBeams();
        Assert.That(drawn, Is.GreaterThan(0), "the client drew no beam of its own");

        await RunTicks(10);
        Assert.That(await ClientBeams(), Is.EqualTo(drawn), "the shooter was sent the server's beam as well");
    }

    [Test]
    public async Task PredictedDiffractionDrawsOnceAndCleansUp()
    {
        await Server.WaitPost(() => SEntMan.SpawnEntity("PlasmaWindow", SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(2f, 0f))));
        var gun = SEntMan.GetNetEntity(await ArmPlayer("WFPredTestDiffractLaser"));
        var aim = new NetCoordinates(Player, new Vector2(5f, 0f));

        await ClientShoot(gun, aim);
        var drawn = await ClientBeams();
        Assert.That(drawn, Is.GreaterThan(3), "the client did not split the beam at the window");

        await RunTicks(10);
        Assert.That(await ClientBeams(), Is.EqualTo(drawn), "the shooter was sent the server's split beams as well");

        var clientLeft = 0;
        var serverLeft = 0;
        await Client.WaitPost(() => clientLeft = CEntMan.Count<HitscanAmmoComponent>());
        await Server.WaitPost(() => serverLeft = SEntMan.Count<HitscanAmmoComponent>());
        Assert.Multiple(() =>
        {
            Assert.That(clientLeft, Is.Zero, "the client leaked hitscan entities");
            Assert.That(serverLeft, Is.Zero, "the server leaked hitscan entities");
        });
    }

    [Test]
    public async Task ServerOnlyShotIsNotMarked()
    {
        var gun = await ArmPlayer("WFPredTestGun");

        await Server.WaitPost(() =>
        {
            var gunSystem = SEntMan.System<SharedGunSystem>();
            gunSystem.AttemptShoot(SPlayer, gun, SEntMan.GetComponent<GunComponent>(gun), new EntityCoordinates(SPlayer, new Vector2(5f, 0f)));
        });
        await RunTicks(3);

        var marked = await ServerCopies();
        var fired = 0;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<ProjectileComponent>();
            while (query.MoveNext(out _, out _))
            {
                fired++;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(fired, Is.EqualTo(1), "server did not fire");
            Assert.That(marked, Is.Empty, "a server-only shot was marked as predicted");
        });
    }
}
