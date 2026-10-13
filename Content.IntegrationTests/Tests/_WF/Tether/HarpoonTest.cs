using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Tether;
using Content.Server.Power.Components;
using Content.Shared._WF.Tether;
using Content.Shared._Mono.CCVar;
using Content.Shared._WF.Tether.Harpoon;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Tether;

/// <summary>
/// The harpoon turret: manning it, the shot-well rule, and the tow cable a good shot leaves behind.
/// </summary>
[TestFixture]
public sealed class HarpoonTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: ropeType
  id: WFTestTowCable
  stiffness: 400000
  dampingRatio: 0.7
  maxStretch: 0.05
  breakForce: 0
  maxLength: 100

- type: entity
  id: WFTestHarpoonTurret
  parent: WFBaseShipHarpoonTurret
  components:
  - type: ShipHarpoonTurret
    ropeType: WFTestTowCable
    arc: 120
  - type: BallisticAmmoProvider
    proto: WFShipHarpoon
    capacity: 1
    cycleable: false

# Loaded by hand, as the real turret is.
- type: entity
  id: WFTestHarpoonTurretEmpty
  parent: WFBaseShipHarpoonTurret
  components:
  - type: ShipHarpoonTurret
    ropeType: WFTestTowCable
    arc: 120

# No spread and no recoil, so a shot flies exactly where it is aimed.
- type: entity
  id: WFTestHarpoonTurretSteady
  parent: WFTestHarpoonTurret
  components:
  - type: Gun
    minAngle: 0
    maxAngle: 0
    recoil: 0
";

    private const string Operator = "MobHuman";
    private const string Wall = "WallSolid";
    private const string DefaultTurret = "WFTestHarpoonTurret";
    private const string SteadyTurret = "WFTestHarpoonTurretSteady";
    private const string EmptyTurret = "WFTestHarpoonTurretEmpty";
    private const string Harpoon = "WFShipHarpoon";

    [Test]
    public async Task ManningGrantsAndRemovesTheControls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid grid = default, turret = default, user = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            grid = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
            turret = entities.SpawnEntity(DefaultTurret, new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
            user = entities.SpawnEntity(Operator, new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<SharedBuckleSystem>().TryBuckle(user, null, turret), Is.True,
                "The operator must be able to strap into the mount.");

            Assert.Multiple(() =>
            {
                Assert.That(entities.TryGetComponent<MannedTurretOperatorComponent>(user, out var manned), Is.True);
                Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Operator,
                    Is.EqualTo(entities.GetNetEntity(user)));
                Assert.That(entities.GetComponent<ActionsComponent>(user).Actions, Has.Count.GreaterThanOrEqualTo(3),
                    "Manning grants the reel and release controls.");
            });

            // The whole point of the upstream hook: the operator's shoot input reaches the turret's gun.
            Assert.That(entities.System<SharedGunSystem>().TryGetGun(user, out var gun), Is.True);
            Assert.That(gun.Owner, Is.EqualTo(turret));

            entities.System<SharedBuckleSystem>().Unbuckle(user, null);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<MannedTurretOperatorComponent>(user), Is.False);
                Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Operator, Is.Null);
                Assert.That(entities.System<SharedGunSystem>().TryGetGun(user, out _), Is.False);
            });

            entities.DeleteEntity(user);
            entities.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StraightShotEmbedsAndTiesOffToBothGrids()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid gridA = default, gridB = default, turret = default, user = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            gridA = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
            gridB = MakeGrid(entities, maps, map.MapId, new Vector2(10f, 0f), 3);
            entities.SpawnEntity(Wall, new EntityCoordinates(gridB, new Vector2(0.5f, 1.5f)));
            (turret, user) = MakeTurret(entities, gridA);
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
        {
            Man(entities, user, turret);
            Fire(entities, user, turret, new MapCoordinates(new Vector2(10.5f, 1.5f), map.MapId));
        });

        await server.WaitRunTicks(40);
        await server.WaitAssertion(() =>
        {
            var rope = entities.System<RopeSystem>();
            var turretComp = entities.GetComponent<ShipHarpoonTurretComponent>(turret);
            Assert.That(turretComp.Harpoon, Is.Not.Null, "A good shot stays the turret's harpoon.");
            var harpoon = entities.GetEntity(turretComp.Harpoon!.Value);

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<ShipHarpoonComponent>(harpoon).Embedded, Is.True,
                    "A square-on shot at speed sinks in.");
                Assert.That(entities.GetComponent<RopeAttachPointComponent>(turret).Ropes, Has.Count.EqualTo(1),
                    "Embedding ties the tow cable off at the turret.");
                Assert.That(entities.HasComponent<RopeAttachPointComponent>(harpoon), Is.True);
            });

            Assert.That(rope.TryGetBody(turret, out var bodyA, out _), Is.True);
            Assert.That(rope.TryGetBody(harpoon, out var bodyB, out _), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(bodyA, Is.EqualTo(gridA), "The near end pulls on the firing hull.");
                Assert.That(bodyB, Is.EqualTo(gridB), "The far end pulls on the struck hull.");
            });
        });

        // Reeling in shortens the cable.
        float before = 0f, after = 0f;
        await server.WaitPost(() =>
        {
            var turretComp = entities.GetComponent<ShipHarpoonTurretComponent>(turret);
            before = entities.GetComponent<RopeComponent>(turretComp.Rope!.Value).Length;
            Assert.That(entities.HasComponent<MannedTurretOperatorComponent>(user), Is.True,
                "Firing must not throw the operator out of the seat.");
            entities.EventBus.RaiseLocalEvent(user, (object) new HarpoonReelInActionEvent(), true);
            Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Reeling, Is.EqualTo(1),
                "The reel control starts the winch.");
        });

        await server.WaitRunTicks(20);
        await server.WaitAssertion(() =>
        {
            var turretComp = entities.GetComponent<ShipHarpoonTurretComponent>(turret);
            Assert.That(turretComp.Rope, Is.Not.Null);
            after = entities.GetComponent<RopeComponent>(turretComp.Rope!.Value).Length;
            Assert.That(after, Is.LessThan(before), "The winch takes the cable in.");

            // Releasing cuts the cable at the turret and leaves the harpoon where it is.
            entities.EventBus.RaiseLocalEvent(user, (object) new HarpoonReleaseActionEvent(), true);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<RopeAttachPointComponent>(turret).Ropes, Is.Empty);
                Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Rope, Is.Null);
            });

            entities.DeleteEntity(user);
            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShallowShotGlancesOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid gridA = default, gridB = default, turret = default, user = default, harpoon = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            gridA = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
            // A long flat wall, so the shallow shot cannot sneak into a face it happens to meet square on.
            gridB = MakeGrid(entities, maps, map.MapId, new Vector2(10f, 0f), 1, 20);
            for (var y = 0; y < 20; y++)
            {
                entities.SpawnEntity(Wall, new EntityCoordinates(gridB, new Vector2(0.5f, y + 0.5f)));
            }

            (turret, user) = MakeTurret(entities, gridA);
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
        {
            Man(entities, user, turret);
            // 57 degrees off the wall's normal: inside the turret's arc, well outside what the barbs can bite.
            Fire(entities, user, turret, new MapCoordinates(new Vector2(10.5f, 14f), map.MapId));
            harpoon = entities.GetEntity(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Harpoon!.Value);
        });

        await server.WaitRunTicks(40);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<ShipHarpoonComponent>(harpoon).Embedded, Is.False,
                    "A shot that comes in at a shallow angle skips off.");
                Assert.That(entities.HasComponent<EmbeddableProjectileComponent>(harpoon), Is.False);
                Assert.That(entities.GetComponent<RopeAttachPointComponent>(turret).Ropes, Is.Empty,
                    "A glancing hit leaves no cable behind.");
            });

            entities.DeleteEntity(user);
            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A harpoon is wider than its point, so one landing beside the seam between two wall tiles also touches the
    /// neighbour of the tile it strikes. Leaning towards the seam, it used to be judged against the neighbour's
    /// hidden side face and skip off.
    /// </summary>
    [TestCase(-0.1f, 0f)]
    [TestCase(0.1f, 0f)]
    [TestCase(-0.1f, 25f)]
    public async Task ShotBesideASeamBites(float lean, float degrees)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid gridA = default, gridB = default, turret = default, user = default, harpoon = default;
        var turn = Angle.FromDegrees(degrees);

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            gridA = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
            // The shot lands a tenth of a tile short of a seam, on the side it is leaning towards.
            gridB = MakeGrid(entities, maps, map.MapId, turn.RotateVec(new Vector2(10f, 0.5f + 2f * lean)), 1, 20);
            // The whole scene turned as one: hulls are rarely square to the map.
            entities.System<SharedTransformSystem>().SetWorldRotation(gridA, turn);
            entities.System<SharedTransformSystem>().SetWorldRotation(gridB, turn);
            for (var y = 0; y < 20; y++)
            {
                entities.SpawnEntity(Wall, new EntityCoordinates(gridB, new Vector2(0.5f, y + 0.5f)));
            }

            (turret, user) = MakeTurret(entities, gridA, SteadyTurret);
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
        {
            Man(entities, user, turret);
            Fire(entities, user, turret, new MapCoordinates(turn.RotateVec(new Vector2(10.5f, 1.5f + lean)), map.MapId));
            harpoon = entities.GetEntity(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Harpoon!.Value);
        });

        await server.WaitRunTicks(40);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<ShipHarpoonComponent>(harpoon).Embedded, Is.True,
                "A square-on shot bites wherever on the wall it lands.");
            Assert.That(entities.System<RopeSystem>().TryGetBody(harpoon, out var body, out _), Is.True);
            Assert.That(body, Is.EqualTo(gridB));

            entities.DeleteEntity(user);
            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LooseHarpoonsHurtNobody()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid grid = default, user = default, spent = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            grid = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
            var coordinates = new EntityCoordinates(grid, new Vector2(1.5f, 1.5f));
            user = entities.SpawnEntity(Operator, coordinates);

            // A vendor stack dropped at the buyer's feet.
            for (var i = 0; i < 4; i++)
            {
                entities.SpawnEntity(Harpoon, coordinates);
            }

            // One left lying on the deck after a clean miss, still marked as fired.
            spent = entities.SpawnEntity(Harpoon, coordinates);
            var projectile = entities.GetComponent<ProjectileComponent>(spent);
            projectile.Weapon = grid;
            projectile.Shooter = grid;
        });

        await server.WaitRunTicks(30);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                // Only piercing: the test map has no air, so the mob takes some pressure damage regardless.
                Assert.That(entities.GetComponent<DamageableComponent>(user).Damage.DamageDict
                        .GetValueOrDefault("Piercing").Float(), Is.Zero,
                    "A harpoon that is not in flight must not hurt whoever it lands on.");
                Assert.That(entities.GetComponent<ProjectileComponent>(spent).Weapon, Is.Null,
                    "A stopped harpoon goes back to being a plain item.");
            });

            entities.DeleteEntity(user);
            entities.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Fast projectiles are swept ahead of themselves each tick. A harpoon that has not been fired is not one,
    /// however fast the ship carrying it goes: it used to be pulled out of its turret and thrown through the hull.
    /// </summary>
    [Test]
    public async Task UnfiredHarpoonsRideAFastShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var threshold = config.GetCVar(MonoCVars.ProjectileRaycastSpeedThreshold);

        EntityUid gridA = default, gridB = default, turret = default, user = default, loaded = default, loose = default;
        var deck = new Vector2(0.5f, 1.5f);

        try
        {
            await server.WaitPost(() =>
            {
                // Any ship counts as fast, so the test does not need one doing 75 m/s.
                config.SetCVar(MonoCVars.ProjectileRaycastSpeedThreshold, 1f);
                entities.DeleteEntity(map.Grid);
                gridA = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
                gridB = MakeGrid(entities, maps, map.MapId, new Vector2(10f, 0f), 3);
                entities.SpawnEntity(Wall, new EntityCoordinates(gridB, new Vector2(0.5f, 1.5f)));
                (turret, user) = MakeTurret(entities, gridA, EmptyTurret);
                loaded = entities.SpawnEntity(Harpoon, new EntityCoordinates(gridA, new Vector2(1.5f, 1.5f)));
                loose = entities.SpawnEntity(Harpoon, new EntityCoordinates(gridA, deck));
            });

            await server.WaitRunTicks(5);
            await server.WaitPost(() =>
            {
                var load = new InteractUsingEvent(user, loaded, turret, entities.GetComponent<TransformComponent>(turret).Coordinates);
                entities.EventBus.RaiseLocalEvent(turret, load);
                Assert.That(load.Handled, Is.True);
                Man(entities, user, turret);
                entities.System<SharedPhysicsSystem>().SetLinearVelocity(gridA, new Vector2(0f, 4f));
            });

            await server.WaitRunTicks(30);
            await server.WaitPost(() =>
            {
                var looseXform = entities.GetComponent<TransformComponent>(loose);
                Assert.Multiple(() =>
                {
                    Assert.That(entities.System<SharedContainerSystem>().IsEntityInContainer(loaded), Is.True,
                        "A loaded harpoon stays in its turret while the ship moves.");
                    Assert.That(looseXform.ParentUid, Is.EqualTo(gridA));
                    Assert.That(looseXform.LocalPosition, Is.EqualTo(deck).Using<Vector2>((a, b) => (a - b).Length() < 0.01f),
                        "A loose harpoon stays where it was put down.");
                });

                var physics = entities.System<SharedPhysicsSystem>();
                entities.System<SharedTransformSystem>().SetWorldPositionRotation(gridA, Vector2.Zero, Angle.Zero);
                physics.SetLinearVelocity(gridA, Vector2.Zero);
                Fire(entities, user, turret, new MapCoordinates(new Vector2(10.5f, 1.5f), map.MapId));
            });

            await server.WaitRunTicks(40);
            await server.WaitAssertion(() =>
            {
                Assert.That(entities.GetComponent<ShipHarpoonComponent>(loaded).Embedded, Is.True,
                    "It still fires and bites after the ride.");

                entities.DeleteEntity(user);
                entities.DeleteEntity(gridA);
                entities.DeleteEntity(gridB);
            });
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(MonoCVars.ProjectileRaycastSpeedThreshold, threshold));
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LooseHarpoonStaysInsideTheHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid grid = default, harpoon = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            grid = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 5);
            for (var y = 0; y < 5; y++)
            {
                entities.SpawnEntity(Wall, new EntityCoordinates(grid, new Vector2(0.5f, y + 0.5f)));
            }

            harpoon = entities.SpawnEntity(Harpoon, new EntityCoordinates(grid, new Vector2(2.5f, 2.5f)));
        });

        await server.WaitRunTicks(5);
        // Kicked across the deck at the hull wall, as a dropped or blown item is.
        await server.WaitPost(() => entities.System<SharedPhysicsSystem>().SetLinearVelocity(harpoon, new Vector2(-6f, 0f)));

        float early = 0f, late = 0f;
        await server.WaitRunTicks(30);
        await server.WaitPost(() => early = entities.GetComponent<PhysicsComponent>(harpoon).LinearVelocity.Length());
        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            late = entities.GetComponent<PhysicsComponent>(harpoon).LinearVelocity.Length();
            var xform = entities.GetComponent<TransformComponent>(harpoon);
            Assert.Multiple(() =>
            {
                Assert.That(xform.ParentUid, Is.EqualTo(grid), "A loose harpoon cannot leave through a wall.");
                Assert.That(xform.LocalPosition.X, Is.GreaterThan(1f));
                Assert.That(late, Is.LessThan(early).Or.Zero, "The deck slows it down like any other item.");
            });

            entities.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WinchStopsWhenTheHullsTouch()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid gridA = default, gridB = default, turret = default, user = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            gridA = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
            gridB = MakeGrid(entities, maps, map.MapId, new Vector2(10f, 0f), 3);
            // The wall is on the far side, so the cable is still two tiles long when the hulls meet.
            entities.SpawnEntity(Wall, new EntityCoordinates(gridB, new Vector2(2.5f, 1.5f)));
            (turret, user) = MakeTurret(entities, gridA, SteadyTurret);
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
        {
            Man(entities, user, turret);
            Fire(entities, user, turret, new MapCoordinates(new Vector2(12.5f, 1.5f), map.MapId));
        });

        await server.WaitRunTicks(40);
        await server.WaitPost(() =>
        {
            Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Rope, Is.Not.Null);
            entities.EventBus.RaiseLocalEvent(user, (object) new HarpoonReelInActionEvent(), true);
        });

        await server.WaitRunTicks(400);
        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<ShipHarpoonTurretComponent>(turret);
            Assert.That(comp.Rope, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Reeling, Is.Zero, "The winch stops once hull meets hull.");
                Assert.That(entities.GetComponent<RopeComponent>(comp.Rope!.Value).Length, Is.GreaterThan(2f),
                    "It does not go on hauling the two hulls into each other.");
            });

            entities.DeleteEntity(user);
            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Fired the ordinary way, and again as a round fast enough to be swept ahead of itself each tick, where the
    /// first flight's sweep used to carry over and send the second shot down the first one's path.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task RecoveredHarpoonBitesAgain(bool swept)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var threshold = config.GetCVar(MonoCVars.ProjectileRaycastSpeedThreshold);

        EntityUid gridA = default, gridB = default, turret = default, user = default, harpoon = default;

        try
        {
            await server.WaitPost(() =>
            {
                if (swept)
                    config.SetCVar(MonoCVars.ProjectileRaycastSpeedThreshold, 1f);

                entities.DeleteEntity(map.Grid);
                gridA = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
                gridB = MakeGrid(entities, maps, map.MapId, new Vector2(10f, 0f), 1, 20);
                for (var y = 0; y < 20; y++)
                {
                    entities.SpawnEntity(Wall, new EntityCoordinates(gridB, new Vector2(0.5f, y + 0.5f)));
                }

                (turret, user) = MakeTurret(entities, gridA);
            });

            await server.WaitRunTicks(5);
            await server.WaitPost(() =>
            {
                Man(entities, user, turret);
                Fire(entities, user, turret, new MapCoordinates(new Vector2(10.5f, 14f), map.MapId));
                harpoon = entities.GetEntity(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Harpoon!.Value);
            });

            await server.WaitRunTicks(40);
            await server.WaitPost(() =>
            {
                Assert.That(entities.GetComponent<ShipHarpoonComponent>(harpoon).Embedded, Is.False,
                    "The first shot has to glance for this test to mean anything.");

                // Pick the harpoon back up and load it, the way a player recovers a miss.
                var reload = new InteractUsingEvent(user, harpoon, turret, entities.GetComponent<TransformComponent>(turret).Coordinates);
                entities.EventBus.RaiseLocalEvent(turret, reload);
                Assert.That(reload.Handled, Is.True, "The turret must take the recovered harpoon back.");
                entities.GetComponent<GunComponent>(turret).NextFire = TimeSpan.Zero;

                // The first shot's recoil spins the tiny test hull; put it back so the second one is square on again.
                var physics = entities.System<SharedPhysicsSystem>();
                var transform = entities.System<SharedTransformSystem>();
                transform.SetWorldPositionRotation(gridA, Vector2.Zero, Angle.Zero);
                physics.SetLinearVelocity(gridA, Vector2.Zero);
                physics.SetAngularVelocity(gridA, 0f);
                Fire(entities, user, turret, new MapCoordinates(new Vector2(10.5f, 1.5f), map.MapId));
            });

            await server.WaitRunTicks(40);
            await server.WaitAssertion(() =>
            {
                Assert.That(entities.GetComponent<ShipHarpoonComponent>(harpoon).Embedded, Is.True,
                    "A recovered harpoon fired square on sinks in like a fresh one.");

                entities.DeleteEntity(user);
                entities.DeleteEntity(gridA);
                entities.DeleteEntity(gridB);
            });
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(MonoCVars.ProjectileRaycastSpeedThreshold, threshold));
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArcFollowsHowTheTurretIsTurned()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid grid = default, user = default, turret = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            grid = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
            var center = new EntityCoordinates(grid, new Vector2(1.5f, 1.5f));
            user = entities.SpawnEntity(Operator, center);

            // Unpack a flatpack turned to face east.
            var flatpack = entities.SpawnEntity("WFShipHarpoonTurretFlatpack", center);
            var transform = entities.System<SharedTransformSystem>();
            transform.SetLocalRotation(flatpack, new Vector2(1f, 0f).ToWorldAngle());
            var tool = entities.SpawnEntity("Multitool", center);
            var unpack = new InteractUsingEvent(user, tool, flatpack, center);
            entities.EventBus.RaiseLocalEvent(flatpack, unpack);
            Assert.That(unpack.Handled, Is.True);
            entities.DeleteEntity(tool);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var query = entities.EntityQueryEnumerator<ShipHarpoonTurretComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    turret = uid;
            }

            Assert.That(turret, Is.Not.EqualTo(EntityUid.Invalid), "Unpacking must build the turret.");

            var system = entities.System<SharedShipHarpoonTurretSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var comp = entities.GetComponent<ShipHarpoonTurretComponent>(turret);
            var east = transform.ToCoordinates(new MapCoordinates(new Vector2(10f, 1.5f), map.MapId));
            var north = transform.ToCoordinates(new MapCoordinates(new Vector2(1.5f, 10f), map.MapId));
            Assert.Multiple(() =>
            {
                Assert.That(system.InArc((turret, comp), east), Is.True, "It fires the way the flatpack faced.");
                Assert.That(system.InArc((turret, comp), north), Is.False);
            });

            // Rotating it after placement turns the arc with it.
            transform.SetLocalRotation(turret, new Vector2(0f, 1f).ToWorldAngle());
            Assert.Multiple(() =>
            {
                Assert.That(system.InArc((turret, comp), north), Is.True, "A rotated turret fires the new way.");
                Assert.That(system.InArc((turret, comp), east), Is.False);
            });

            // No grid power here; a turret without a receiver counts as powered and will take an operator.
            entities.RemoveComponent<ApcPowerReceiverComponent>(turret);

            // A manned turret's rotation is its aim, and leaves the mount alone.
            Assert.That(entities.System<SharedBuckleSystem>().TryBuckle(user, null, turret), Is.True);
            Assert.That(comp.Operator, Is.EqualTo(entities.GetNetEntity(user)));
            transform.SetLocalRotation(turret, new Vector2(1f, 1f).ToWorldAngle());
            Assert.That(system.InArc((turret, comp), north), Is.True, "Aiming must not move the mount.");

            entities.System<SharedBuckleSystem>().Unbuckle(user, null);
            Assert.That(entities.GetComponent<TransformComponent>(turret).LocalRotation.EqualsApprox(comp.MountRotation),
                Is.True, "Letting go returns the turret to rest.");

            entities.DeleteEntity(user);
            entities.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TurretRefusesHandCoils()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var grid = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
            var (turret, user) = MakeTurret(entities, grid);
            var coil = entities.SpawnEntity("WFRopeTowCableCoil", entities.GetComponent<TransformComponent>(user).Coordinates);

            // A tow cable coil tied to the drum would take the turret's only rope slot and stop it firing.
            var click = new AfterInteractEvent(user, coil, turret, entities.GetComponent<TransformComponent>(turret).Coordinates, true);
            entities.EventBus.RaiseLocalEvent(coil, click);
            Assert.Multiple(() =>
            {
                Assert.That(click.Handled, Is.True, "The refusal still consumes the click.");
                Assert.That(entities.HasComponent<RopeCarrierComponent>(user), Is.False,
                    "No loose end is taken off the coil at a harpoon turret.");
            });

            entities.DeleteEntity(user);
            entities.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A turret bolted to the east edge of a hull, facing east, with an operator standing on it.</summary>
    private static (EntityUid Turret, EntityUid User) MakeTurret(IEntityManager entities, EntityUid grid, string prototype = DefaultTurret)
    {
        var coordinates = new EntityCoordinates(grid, new Vector2(2.5f, 1.5f));
        var turret = entities.SpawnEntity(prototype, coordinates);
        var comp = entities.GetComponent<ShipHarpoonTurretComponent>(turret);
        comp.MountRotation = new Vector2(1f, 0f).ToWorldAngle();
        return (turret, entities.SpawnEntity(Operator, coordinates));
    }

    private static void Man(IEntityManager entities, EntityUid user, EntityUid turret)
    {
        Assert.That(entities.System<SharedBuckleSystem>().TryBuckle(user, null, turret), Is.True);
        Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Operator,
            Is.EqualTo(entities.GetNetEntity(user)));
    }

    private static void Fire(IEntityManager entities, EntityUid user, EntityUid turret, MapCoordinates target)
    {
        var gun = entities.GetComponent<GunComponent>(turret);
        var transform = entities.System<SharedTransformSystem>();
        entities.System<SharedGunSystem>().AttemptShoot(user, turret, gun, transform.ToCoordinates(target));
        Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Harpoon, Is.Not.Null,
            "The turret must actually have loosed a harpoon.");
    }

    /// <summary>A free-floating rectangular hull.</summary>
    private static EntityUid MakeGrid(IEntityManager entities, IMapManager maps, MapId map, Vector2 position, int width, int height = 0)
    {
        var mapSystem = entities.System<SharedMapSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var transform = entities.System<SharedTransformSystem>();

        var grid = maps.CreateGridEntity(map);
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < (height == 0 ? width : height); y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
        physics.SetBodyType(grid.Owner, BodyType.Dynamic);
        transform.SetWorldPosition(grid.Owner, position);
        return grid.Owner;
    }
}
