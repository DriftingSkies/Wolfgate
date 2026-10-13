#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Animations;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._RMC14.Weapons.Ranged.Prediction;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.CombatMode;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using ClientGunSystem = Content.Client.Weapons.Ranged.Systems.GunSystem;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// A gun mounted on a vehicle and fired by a crewman on another map: where the shot starts, that it is one shot, that
/// it clears its own hull, and that a hand-held gun is unchanged.
/// </summary>
public sealed class VehicleGunHookTest : InteractionTest
{
    private const string Mounted = "WFVehicleGunTestMounted";
    private const string MountedControl = "WFVehicleGunTestMountedControl";
    private const string MountedArc = "WFVehicleGunTestMountedArc";
    private const string HandHeld = "WFVehicleGunTestHandHeld";

    /// <summary>Where the muzzle sits from the vehicle's centre, inside its hull, on an unturned grid.</summary>
    private static readonly Vector2 MuzzleOffset = new(0.2f, 0.8f);

    protected override string PlayerPrototype => "WFVehicleGunTestMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFVehicleGunTestMob
  parent: InteractionTestMob
  components:
  - type: CombatMode

- type: entity
  id: WFVehicleGunTestBullet
  parent: BaseBullet
  categories: [ HideSpawnMenu ]

- type: entity
  id: WFVehicleGunTestCartridge
  categories: [ HideSpawnMenu ]
  components:
  - type: CartridgeAmmo
    proto: WFVehicleGunTestBullet
    deleteOnSpawn: true

- type: entity
  id: WFVehicleGunTestGunBase
  parent: BaseItem
  abstract: true
  components:
  - type: Gun
    fireRate: 4
    recoil: 0
    minAngle: 0
    maxAngle: 0
    angleIncrease: 0
    projectileSpeed: 30
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    proto: WFVehicleGunTestCartridge
    capacity: 20

- type: entity
  id: WFVehicleGunTestMounted
  parent: WFVehicleGunTestGunBase
  components:
  - type: GunUseGunOrigin
  - type: GunIgnoreContainerOwnerCollision
  - type: GunMuzzleOffset
    offset: 0.2,0.8

- type: entity
  id: WFVehicleGunTestMountedControl
  parent: WFVehicleGunTestGunBase
  components:
  - type: GunUseGunOrigin
  - type: GunMuzzleOffset
    offset: 0.2,0.8

- type: entity
  id: WFVehicleGunTestMountedArc
  parent: WFVehicleGunTestGunBase
  components:
  - type: GunUseGunOrigin
  - type: GunFireArc
    arc: 90

- type: entity
  id: WFVehicleGunTestHandHeld
  parent: WFVehicleGunTestGunBase

- type: entity
  parent: WFVehicleTestChassis
  id: WFVehicleGunTestVehicle
  components:
  - type: Damageable
    damageContainer: StructuralInorganic
  - type: ItemSlots
    slots:
      gun:
        whitelist:
          components:
          - Gun
";

    private sealed class Rig
    {
        public EntityUid Vehicle;
        public EntityUid Gun;
        public EntityUid MapB;
        public EntityUid Operator;
    }

    /// <summary>
    /// A vehicle with the gun mounted on the test grid, and a second, separate map for the crewman. With no operator
    /// given, a fresh mob is spawned on that second map.
    /// </summary>
    private async Task<Rig> BuildRig(string gunPrototype, EntityUid? gunner = null)
    {
        var rig = new Rig();
        await Server.WaitPost(() =>
        {
            var plating = TileMan[Plating].TileId;
            for (var x = 0; x < 12; x++)
            {
                for (var y = 0; y < 12; y++)
                {
                    MapSystem.SetTile(MapData.Grid.Owner, MapData.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            rig.Vehicle = SEntMan.SpawnEntity("WFVehicleGunTestVehicle", new EntityCoordinates(MapData.Grid.Owner, 5f, 5f));
            Transform.SetLocalRotation(rig.Vehicle, Angle.Zero);

            rig.Gun = SEntMan.SpawnEntity(gunPrototype, new EntityCoordinates(MapData.Grid.Owner, 1f, 1f));
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(rig.Vehicle, "gun", rig.Gun, null), "The gun did not mount.");

            rig.MapB = MapSystem.CreateMap(out _);
            rig.Operator = gunner ?? SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.MapB, Vector2.Zero));

            var operatorComp = SEntMan.EnsureComponent<VehicleWeaponsOperatorComponent>(rig.Operator);
            operatorComp.Vehicle = rig.Vehicle;
            operatorComp.SelectedWeapon = rig.Gun;
            SEntMan.Dirty(rig.Operator, operatorComp);
        });
        await RunTicks(5);

        return rig;
    }

    /// <summary>Fires the rig's gun the way a gunner's request does, aiming at a point relative to the gun.</summary>
    private void Shoot(Rig rig, Vector2 aim)
    {
        var guns = SEntMan.System<SharedGunSystem>();
        guns.AttemptShoot(rig.Operator, rig.Gun, SEntMan.GetComponent<GunComponent>(rig.Gun), new EntityCoordinates(rig.Gun, aim));
    }

    private List<EntityUid> Projectiles()
    {
        var found = new List<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<ProjectileComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            found.Add(uid);
        }

        return found;
    }

    /// <summary>The shot starts at the muzzle on the vehicle's map, as one projectile aimed at the target, though its gunner is on another map.</summary>
    [Test]
    public async Task ShotFromAnotherMapStartsAtTheMuzzleOnTheVehiclesMap()
    {
        var rig = await BuildRig(Mounted);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Transform.GetMapId(rig.Operator), Is.Not.EqualTo(MapId), "The test needs the gunner on another map.");

            var vehiclePos = Transform.GetWorldPosition(rig.Vehicle);
            var muzzle = vehiclePos + MuzzleOffset;
            var aim = new Vector2(6f, 0f);
            var target = vehiclePos + aim;

            Shoot(rig, aim);

            var shots = Projectiles();
            Assert.That(shots, Has.Count.EqualTo(1), "A shot should be exactly one projectile.");

            var shot = shots[0];
            var projectile = SEntMan.GetComponent<ProjectileComponent>(shot);
            var velocity = SEntMan.GetComponent<PhysicsComponent>(shot).LinearVelocity;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Transform.GetMapId(shot), Is.EqualTo(MapId), "The projectile is not on the vehicle's map.");
                Assert.That(Vector2.Distance(Transform.GetWorldPosition(shot), muzzle), Is.LessThan(0.01f), "The projectile did not start at the muzzle.");
                Assert.That(projectile.Shooter, Is.EqualTo(rig.Operator));
                Assert.That(projectile.Weapon, Is.EqualTo(rig.Gun));
                Assert.That(velocity.Length(), Is.EqualTo(30f).Within(0.1f));
                Assert.That(Math.Abs(Angle.ShortestDistance(new Angle(velocity), new Angle(target - muzzle)).Theta), Is.LessThan(0.001),
                    "The projectile is not flying at the target.");
            }
        });
    }

    /// <summary>The operator's selected weapon answers the gun lookup, and clearing it stops the answer.</summary>
    [Test]
    public async Task TheSelectedWeaponAnswersTheGunLookup()
    {
        var rig = await BuildRig(Mounted);

        await Server.WaitAssertion(() =>
        {
            var guns = SEntMan.System<SharedGunSystem>();
            Assert.That(guns.TryGetGun(rig.Operator, out var found, out var comp), Is.True);
            Assert.That(found, Is.EqualTo(rig.Gun));
            Assert.That(comp, Is.Not.Null);

            SEntMan.GetComponent<VehicleWeaponsOperatorComponent>(rig.Operator).SelectedWeapon = null;
            Assert.That(guns.TryGetGun(rig.Operator, out _, out _), Is.False, "An operator with nothing selected should have no gun.");
        });
    }

    /// <summary>With the component the shot clears its own hull; the same gun without it hits the hull, so the handler is what spares it.</summary>
    [Test]
    public async Task AShotDoesNotHitItsOwnHull()
    {
        var rig = await BuildRig(Mounted);
        var shot = EntityUid.Invalid;
        var start = Vector2.Zero;

        await Server.WaitAssertion(() =>
        {
            Shoot(rig, new Vector2(6f, 0f));
            shot = Projectiles().Single();
            start = Transform.GetWorldPosition(shot);
        });
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.EntityExists(shot), Is.True, "The projectile is gone; it hit something.");
                Assert.That(SEntMan.GetComponent<ProjectileComponent>(shot).ProjectileSpent, Is.False);
                Assert.That((float) SEntMan.GetComponent<DamageableComponent>(rig.Vehicle).TotalDamage, Is.Zero, "The shot damaged its own hull.");
                Assert.That(Vector2.Distance(Transform.GetWorldPosition(shot), start), Is.GreaterThan(2f), "The projectile never got away from the hull.");
            }
        });
    }

    [Test]
    public async Task WithoutTheComponentAShotHitsItsOwnHull()
    {
        var rig = await BuildRig(MountedControl);

        await Server.WaitAssertion(() => Shoot(rig, new Vector2(6f, 0f)));
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That((float) SEntMan.GetComponent<DamageableComponent>(rig.Vehicle).TotalDamage, Is.GreaterThan(0f),
                "The control shot missed the hull, so the other test proves nothing.");
        });
    }

    /// <summary>A gun that does not use its own origin still fires from its user.</summary>
    [Test]
    public async Task AHandHeldGunStillShootsFromTheUser()
    {
        var user = EntityUid.Invalid;
        var gun = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            user = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(MapData.Grid.Owner, 3.5f, 3.5f));
            gun = SEntMan.SpawnEntity(HandHeld, new EntityCoordinates(MapData.Grid.Owner, 3.5f, 3.5f));
            Assert.That(HandSys.TryPickupAnyHand(user, gun), Is.True, "The gun was not picked up.");
        });

        // Picking a gun up puts it on a full fire-interval cooldown.
        await RunTicks(40);

        await Server.WaitAssertion(() =>
        {
            var userPos = Transform.GetWorldPosition(user);
            SEntMan.System<SharedGunSystem>().AttemptShoot(user, gun, SEntMan.GetComponent<GunComponent>(gun), new EntityCoordinates(user, new Vector2(6f, 0f)));

            var shots = Projectiles();
            Assert.That(shots, Has.Count.EqualTo(1));
            Assert.That(Vector2.Distance(Transform.GetWorldPosition(shots[0]), userPos), Is.LessThan(0.01f), "A hand-held gun should shoot from its user.");
        });
    }

    /// <summary>A shot outside the arc is refused without a cooldown, and one inside it fires and leaves the usual cooldown.</summary>
    [Test]
    public async Task TheFireArcRefusesWithoutLeavingACooldown()
    {
        var rig = await BuildRig(MountedArc);

        await Server.WaitAssertion(() =>
        {
            var gun = SEntMan.GetComponent<GunComponent>(rig.Gun);
            var facing = Transform.GetWorldRotation(rig.Vehicle);
            var allowed = 0;

            foreach (var aim in new[] { new Vector2(0f, -5f), new Vector2(5f, 0f), new Vector2(0f, 5f), new Vector2(-5f, 0f) })
            {
                var inArc = Math.Abs(Angle.ShortestDistance(aim.ToWorldAngle(), facing).Degrees) <= 45;
                var before = Projectiles().Count;

                Shoot(rig, aim);

                var fired = Projectiles().Count - before;
                if (inArc)
                {
                    allowed++;
                    Assert.That(fired, Is.EqualTo(1), $"A shot at {aim} is inside the arc and should fire.");
                    Assert.That(gun.NextFire, Is.GreaterThan(Timing().CurTime), "A shot that fires leaves the gun on cooldown.");

                    // Spend the cooldown so the refusals below are measured on their own.
                    gun.NextFire = TimeSpan.Zero;
                }
                else
                {
                    Assert.That(fired, Is.Zero, $"A shot at {aim} is outside the arc and should be refused.");
                    Assert.That(gun.NextFire, Is.LessThanOrEqualTo(Timing().CurTime), $"A refused shot at {aim} must not leave a cooldown.");
                }
            }

            Assert.That(allowed, Is.EqualTo(1), "Exactly one of the four directions should be inside a 90 degree arc.");

            Assert.That(Server.ResolveDependency<ILocalizationManager>().TryGetString("rmc-gun-arc-blocked", out _), Is.True,
                "The refusal message is missing from the locale.");
        });
    }

    /// <summary>The muzzle keeps its place in the grid's frame when the grid is turned, as a ship's deck can be.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TheMuzzleKeepsItsPlaceWhenTheGridTurns(bool moverFacing)
    {
        var rig = await BuildRig(Mounted);
        var offsets = new List<Vector2>();

        foreach (var degrees in new[] { 0, 90 })
        {
            await Server.WaitAssertion(() =>
            {
                Transform.SetLocalRotation(MapData.Grid.Owner, Angle.FromDegrees(degrees));

                // Either the mover's own facing, which is in the grid's frame, or the transform's.
                if (moverFacing)
                    SEntMan.GetComponent<GridVehicleMoverComponent>(rig.Vehicle).CurrentDirection = new Vector2i(0, 1);
            });
            await RunTicks(2);

            await Server.WaitAssertion(() =>
            {
                Shoot(rig, new Vector2(6f, 0f));
                var shot = Projectiles().Single();

                var gridInverse = Transform.GetInvWorldMatrix(MapData.Grid.Owner);
                var shotLocal = Vector2.Transform(Transform.GetWorldPosition(shot), gridInverse);
                offsets.Add(shotLocal - SEntMan.GetComponent<TransformComponent>(rig.Vehicle).LocalPosition);

                SEntMan.DeleteEntity(shot);
            });

            // Let the gun's cooldown pass before the next shot.
            await RunSeconds(1f);
        }

        Assert.That(offsets, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(offsets[0].Length(), Is.EqualTo(MuzzleOffset.Length()).Within(0.01f), "The muzzle is not where its offset says.");
            Assert.That(Vector2.Distance(offsets[0], offsets[1]), Is.LessThan(0.01f), "Turning the grid moved the muzzle within it.");
        }
    }

    /// <summary>
    /// The gunner's client fires its own copy of the shot. It must start where the server's shot starts, be the one
    /// copy, and be linked to the server's projectile, though the gunner is on another map.
    /// </summary>
    [Test]
    public async Task APredictedShotFromAnotherMapIsOneProjectileAtTheMuzzle()
    {
        var rig = await BuildRig(Mounted, SPlayer);

        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(SPlayer, true);
            Transform.SetCoordinates(SPlayer, new EntityCoordinates(rig.MapB, Vector2.Zero));

            // What the vehicle's viewport does once it exists: keep the vehicle in the gunner's view.
            SEntMan.System<ViewSubscriberSystem>().AddViewSubscriber(rig.Vehicle, ServerSession);
        });
        await RunTicks(15);

        var netGun = SEntMan.GetNetEntity(rig.Gun);
        var cGun = EntityUid.Invalid;
        var cVehicle = EntityUid.Invalid;
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetEntity(netGun, out var foundGun), Is.True, "The gunner's client never received the gun.");
            Assert.That(CEntMan.TryGetEntity(SEntMan.GetNetEntity(rig.Vehicle), out var foundVehicle), Is.True, "The gunner's client never received the vehicle.");
            cGun = foundGun!.Value;
            cVehicle = foundVehicle!.Value;
        });

        var vehiclePos = Vector2.Zero;
        await Server.WaitPost(() => vehiclePos = Transform.GetWorldPosition(rig.Vehicle));
        var muzzle = vehiclePos + MuzzleOffset;

        var aim = new NetCoordinates(netGun, new Vector2(6f, 0f));
        List<int>? slots = null;
        var copyPos = Vector2.Zero;
        var copyMap = MapId.Nullspace;
        var clientMap = MapId.Nullspace;
        await Client.WaitPost(() =>
        {
            var guns = CEntMan.System<ClientGunSystem>();
            slots = guns.ShootRequested(netGun, aim, null, null, ClientSession, true);
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new RequestShootEvent
            {
                Gun = netGun,
                Coordinates = aim,
                Shot = slots,
                Predicted = guns.DrewHitscan,
            });

            if (slots is not { Count: 1 })
                return;

            var xforms = CEntMan.System<SharedTransformSystem>();
            var copy = new EntityUid(slots[0]);
            copyPos = xforms.GetWorldPosition(copy);
            copyMap = xforms.GetMapId(copy);
            clientMap = xforms.GetMapId(cVehicle);
        });

        Assert.That(slots, Is.Not.Null, "The client predicted nothing.");
        Assert.That(slots, Has.Count.EqualTo(1), "The client should predict exactly one projectile.");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(copyMap, Is.EqualTo(clientMap), "The predicted copy is not on the vehicle's map.");
            Assert.That(Vector2.Distance(copyPos, muzzle), Is.LessThan(0.05f), "The predicted copy did not start at the muzzle.");
        }

        // The flash follows the gun, not the gunner on the other map.
        await Client.WaitAssertion(() =>
        {
            var tracked = CEntMan.EntityQueryEnumerator<TrackUserComponent>();
            var found = false;
            while (tracked.MoveNext(out _, out var track))
            {
                found = true;
                Assert.That(track.User, Is.EqualTo(cGun), "The muzzle flash tracks someone other than the gun.");
            }

            Assert.That(found, Is.True, "No muzzle flash was drawn.");
        });

        await RunTicks(10);

        var serverShot = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            var shots = Projectiles();
            Assert.That(shots, Has.Count.EqualTo(1), "The server should have fired exactly one projectile.");
            serverShot = shots[0];

            var linked = SEntMan.GetComponent<PredictedProjectileServerComponent>(shots[0]);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(linked.ClientId, Is.EqualTo(slots![0]), "The server's projectile is not linked to the client's copy.");
                Assert.That(Transform.GetMapId(shots[0]), Is.EqualTo(MapId));
                Assert.That((float) SEntMan.GetComponent<DamageableComponent>(rig.Vehicle).TotalDamage, Is.Zero, "The shot damaged its own hull.");
            }
        });

        await Client.WaitAssertion(() =>
        {
            var copy = new EntityUid(slots![0]);
            Assert.That(CEntMan.EntityExists(copy), Is.True, "The predicted copy vanished; it hit something.");
            Assert.That(CEntMan.GetComponent<ProjectileComponent>(copy).ProjectileSpent, Is.False);
        });

        // Deleting the server's copy drops the predicted one, so teardown is not left with projectiles in flight.
        await Server.WaitPost(() => SEntMan.DeleteEntity(serverShot));
        await RunTicks(5);
        await Client.WaitAssertion(() => Assert.That(CEntMan.EntityExists(new EntityUid(slots![0])), Is.False, "The orphaned predicted copy was not removed."));
    }

    private Robust.Shared.Timing.IGameTiming Timing() => STiming;
}
