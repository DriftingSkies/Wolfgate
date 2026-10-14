#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.Vehicles.Compat;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>The Vehicles module's compatibility helpers, collision bit and damage modifier sets.</summary>
[TestFixture]
public sealed class VehicleCompatTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFVehicleTestHull
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      body:
        shape: !type:PhysShapeCircle
          radius: 0.4
        layer:
        - Vehicle
        mask:
        - Impassable
        hard: true

- type: entity
  id: WFVehicleTestWall
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      body:
        shape: !type:PhysShapeCircle
          radius: 0.4
        layer:
        - Impassable
        hard: true

- type: entity
  id: WFVehicleTestBystander
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      body:
        shape: !type:PhysShapeCircle
          radius: 0.4
        layer:
        - Opaque
        mask:
        - Opaque
        hard: true

- type: entity
  id: WFVehicleTestSoftWall
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      body:
        shape: !type:PhysShapeCircle
          radius: 0.4
        layer:
        - Impassable
        hard: false

- type: entity
  id: WFVehicleTestSoftMask
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      hull:
        shape: !type:PhysShapeCircle
          radius: 0.4
        layer:
        - Vehicle
        hard: true
      sensor:
        shape: !type:PhysShapeCircle
          radius: 0.4
        mask:
        - Impassable
        hard: false

- type: entity
  id: WFVehicleTestTwoHard
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      hull:
        shape: !type:PhysShapeCircle
          radius: 0.4
        layer:
        - Vehicle
        hard: true
      reach:
        shape: !type:PhysShapeCircle
          radius: 0.4
        mask:
        - Impassable
        hard: true
";

    /// <summary>The eight modifier sets copied from RMC-14, with the coefficients they carry there.</summary>
    private static readonly Dictionary<string, Dictionary<string, float>> ModifierSets = new()
    {
        ["VehicleArmorBallistic"] = new() { ["Blunt"] = 0.9f, ["Slash"] = 0.603f, ["Piercing"] = 0.603f, ["Structural"] = 0.9f, ["Heat"] = 0.9f },
        ["VehicleArmorCaustic"] = new() { ["Blunt"] = 0.9f, ["Slash"] = 0.9f, ["Piercing"] = 0.9f, ["Structural"] = 0.9f, ["Heat"] = 0.603f },
        ["VehicleArmorConcussive"] = new() { ["Blunt"] = 0.603f, ["Slash"] = 0.9f, ["Piercing"] = 0.9f, ["Structural"] = 0.9f, ["Heat"] = 0.9f },
        ["VehicleArmorPaladin"] = new() { ["Blunt"] = 0.9f, ["Slash"] = 0.9f, ["Piercing"] = 0.9f, ["Structural"] = 0.603f, ["Heat"] = 0.9f },
        ["VehicleFrameTank"] = new() { ["Blunt"] = 0.8f, ["Slash"] = 0.7f, ["Piercing"] = 0.4f, ["Structural"] = 0.8f, ["Heat"] = 1.5f },
        ["VehicleFrameAPC"] = new() { ["Blunt"] = 0.7f, ["Slash"] = 0.8f, ["Piercing"] = 0.6f, ["Structural"] = 0.7f, ["Heat"] = 1f },
        ["VehicleFrameARC"] = new() { ["Blunt"] = 0.8f, ["Slash"] = 1f, ["Piercing"] = 0.6f, ["Structural"] = 0.8f, ["Heat"] = 1f },
        ["VehicleFrameCLFVan"] = new() { ["Blunt"] = 0.8f, ["Slash"] = 0.9f, ["Piercing"] = 0.8f, ["Structural"] = 0.8f, ["Heat"] = 1f },
    };

    /// <summary>The vehicle bit is the one free bit below the map grid's, and no named group but "everything" uses it.</summary>
    [Test]
    public void VehicleCollisionGroupIsItsOwnBit()
    {
        Assert.That((int) CollisionGroup.Vehicle, Is.EqualTo(1 << 30));

        foreach (var group in Enum.GetValues<CollisionGroup>())
        {
            if (group == CollisionGroup.Vehicle || group == CollisionGroup.AllMask)
                continue;

            Assert.That((int) group & (int) CollisionGroup.Vehicle, Is.Zero, $"{group} overlaps the vehicle bit.");
        }
    }

    /// <summary>
    /// The helper looks at every pair of hard fixtures with each side's own layer and mask. The engine overload
    /// compares the first body's fixtures with themselves, which says no for a vehicle against a wall.
    /// </summary>
    [Test]
    public async Task IsHardCollidableTestsEveryFixturePair()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            Entity<FixturesComponent?, PhysicsComponent?> Spawn(string prototype, float x)
            {
                var uid = entMan.SpawnEntity(prototype, new MapCoordinates(new Vector2(x, 0), map.MapId));
                return (uid, entMan.GetComponent<FixturesComponent>(uid), entMan.GetComponent<PhysicsComponent>(uid));
            }

            var hull = Spawn("WFVehicleTestHull", 10);
            var wall = Spawn("WFVehicleTestWall", 12);
            var bystander = Spawn("WFVehicleTestBystander", 14);
            var softWall = Spawn("WFVehicleTestSoftWall", 16);
            var softMask = Spawn("WFVehicleTestSoftMask", 18);
            var twoHard = Spawn("WFVehicleTestTwoHard", 20);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(VehicleHardCollision.IsHardCollidable(hull, wall), Is.True,
                    "A vehicle whose mask has the wall's layer collides with it.");
                Assert.That(VehicleHardCollision.IsHardCollidable(wall, hull), Is.True,
                    "The answer does not depend on the order.");
                Assert.That(VehicleHardCollision.IsHardCollidable(hull, bystander), Is.False,
                    "Bodies whose layers and masks do not meet do not collide.");
                Assert.That(VehicleHardCollision.IsHardCollidable(hull, softWall), Is.False,
                    "A wall with only soft fixtures is not hard.");
                Assert.That(VehicleHardCollision.IsHardCollidable(softMask, wall), Is.False,
                    "A soft fixture's mask does not count when the hard fixtures do not meet the wall.");
                Assert.That(VehicleHardCollision.IsHardCollidable(twoHard, wall), Is.True,
                    "Any hard fixture's mask counts, not only the first fixture's.");
                Assert.That(VehicleHardCollision.IsHardCollidable(wall, twoHard), Is.True);
                Assert.That(VehicleHardCollision.IsHardCollidable(hull, (wall.Owner, null, null)), Is.False,
                    "A body that is missing its components cannot be hard-collidable.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Teleporting a puller across maps keeps the pull, and teleporting a pulled entity ends the pull.</summary>
    [Test]
    public async Task HandlePullingKeepsThePullAcrossMaps()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var pulling = server.System<PullingSystem>();
        var handler = server.System<VehiclePullingSystem>();
        var maps = server.System<SharedMapSystem>();

        MapId other = default;
        EntityUid puller = default, crate = default, bystander = default, holder = default;

        await server.WaitPost(() =>
        {
            maps.CreateMap(out other);
            puller = entMan.SpawnEntity("MobHuman", map.GridCoords);
            crate = entMan.SpawnEntity("CrateGeneric", map.GridCoords);
            bystander = entMan.SpawnEntity("MobHuman", map.GridCoords);
            holder = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(pulling.TryStartPull(puller, crate), Is.True, "Precondition: the human could not pull the crate.");

            handler.HandlePulling(puller, new MapCoordinates(new Vector2(5, 5), other));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(puller).MapID, Is.EqualTo(other), "The puller did not move.");
                Assert.That(entMan.GetComponent<TransformComponent>(crate).MapID, Is.EqualTo(other), "The pulled crate was left behind.");
                Assert.That(entMan.GetComponent<PullerComponent>(puller).Pulling, Is.EqualTo(crate), "The pull was lost.");
                Assert.That(entMan.GetComponent<PullableComponent>(crate).Puller, Is.EqualTo(puller));
            }
        });

        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<PullerComponent>(puller).Pulling, Is.EqualTo(crate), "The pull did not survive the following ticks.");
                Assert.That(entMan.GetComponent<TransformComponent>(crate).MapID, Is.EqualTo(other));
            }

            Assert.That(pulling.TryStartPull(holder, bystander), Is.True, "Precondition: the human could not pull the other human.");

            handler.HandlePulling(bystander, new MapCoordinates(new Vector2(-5, -5), other));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(bystander).MapID, Is.EqualTo(other), "The pulled entity did not move.");
                Assert.That(entMan.GetComponent<PullerComponent>(holder).Pulling, Is.Null, "The pull of a teleported entity must end.");
                Assert.That(entMan.GetComponent<PullableComponent>(bystander).Puller, Is.Null);
            }
        });

        await server.WaitPost(() => maps.DeleteMap(other));
        await pair.CleanReturnAsync();
    }

    /// <summary>The eight Vehicle* modifier sets load with the coefficients RMC-14 gives them.</summary>
    [Test]
    public async Task VehicleDamageModifierSetsLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ProtoMan;

        await pair.Server.WaitAssertion(() =>
        {
            foreach (var (id, expected) in ModifierSets)
            {
                Assert.That(protos.TryIndex<DamageModifierSetPrototype>(id, out var set), Is.True, $"{id} is missing.");
                Assert.That(set!.Coefficients, Has.Count.EqualTo(expected.Count), id);

                foreach (var (type, coefficient) in expected)
                {
                    Assert.That(set.Coefficients, Contains.Key(type), $"{id} has no {type}.");
                    Assert.That(set.Coefficients[type], Is.EqualTo(coefficient).Within(0.0001f), $"{id} {type}");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A queued refresh reaches every open interface of the type on the next update, and only once.</summary>
    [Test]
    public async Task RefreshUIsRunsOnTheNextUpdate()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        EntityUid owner = default;
        TestBui bui = default!;

        await client.WaitPost(() =>
        {
            owner = client.EntMan.Spawn();
            var ui = client.EntMan.AddComponent<UserInterfaceComponent>(owner);
            bui = new TestBui(owner, TestUiKey.Key);
            ui.ClientOpenInterfaces[TestUiKey.Key] = bui;

            var refresh = client.System<VehicleUserInterfaceSystem>();
            refresh.RefreshUIs<TestBui>(owner);
            refresh.RefreshUIs<TestBui>(owner);
        });

        Assert.That(bui.Refreshes, Is.Zero, "The refresh must wait for the next update.");
        await client.WaitRunTicks(1);
        Assert.That(bui.Refreshes, Is.EqualTo(2), "Each queued refresh runs once.");
        await client.WaitRunTicks(3);
        Assert.That(bui.Refreshes, Is.EqualTo(2), "The queue must be empty afterwards.");

        await client.WaitPost(() =>
        {
            client.EntMan.GetComponent<UserInterfaceComponent>(owner).ClientOpenInterfaces.Clear();
            client.EntMan.DeleteEntity(owner);
        });
        await pair.CleanReturnAsync();
    }

    [Serializable, NetSerializable]
    private enum TestUiKey
    {
        Key,
    }

    private sealed class TestBui : BoundUserInterface, IRefreshableBui
    {
        public int Refreshes;

        public TestBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        public void Refresh()
        {
            Refreshes++;
        }
    }
}
