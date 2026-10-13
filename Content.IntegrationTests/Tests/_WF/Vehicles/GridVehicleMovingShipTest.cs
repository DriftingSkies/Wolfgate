#nullable enable
using System.Numerics;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>A vehicle on a deck that is itself moving, turning and being moved between maps, as a ship does.</summary>
[TestFixture]
public sealed class GridVehicleMovingShipTest
{
    private static readonly Vector2 ShipVelocity = new(1.5f, 0.75f);
    private const float ShipSpin = 0.3f;

    /// <summary>Gives the rig's grid a body that drifts and spins on its own with nothing to slow it.</summary>
    private static async Task SetShipMoving(VehicleRig rig)
    {
        await rig.Pair.Server.WaitPost(() =>
        {
            var physics = rig.Pair.Server.System<SharedPhysicsSystem>();
            var body = rig.Pair.Server.EntMan.GetComponent<PhysicsComponent>(rig.Grid);

            physics.SetBodyType(rig.Grid, BodyType.Dynamic);
            physics.SetSleepingAllowed(rig.Grid, body, false);
            physics.SetLinearDamping(rig.Grid, body, 0f);
            physics.SetAngularDamping(rig.Grid, body, 0f);
            physics.SetLinearVelocity(rig.Grid, ShipVelocity, body: body);
            physics.SetAngularVelocity(rig.Grid, ShipSpin, body: body);
            physics.WakeBody(rig.Grid, body: body);
        });
    }

    private static (Vector2 Position, Angle Rotation) GridPose(VehicleRig rig)
    {
        var xforms = rig.Pair.Server.System<SharedTransformSystem>();
        return (xforms.GetWorldPosition(rig.Grid), xforms.GetWorldRotation(rig.Grid));
    }

    /// <summary>Left alone on a drifting, spinning ship, a vehicle keeps its place and its facing on the deck.</summary>
    [Test]
    public async Task AParkedVehicleStaysPutOnAMovingShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, facingRight: true);
        var server = pair.Server;
        var entMan = server.EntMan;

        var start = Vector2.Zero;
        var startRotation = Angle.Zero;
        var gridStart = (Position: Vector2.Zero, Rotation: Angle.Zero);

        await server.WaitAssertion(() =>
        {
            start = VehicleRigs.LocalPosition(rig);
            startRotation = entMan.GetComponent<TransformComponent>(rig.Vehicle).LocalRotation;
            gridStart = GridPose(rig);
        });

        await SetShipMoving(rig);
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var gridNow = GridPose(rig);
            var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);
            var body = entMan.GetComponent<PhysicsComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That((gridNow.Position - gridStart.Position).Length(), Is.GreaterThan(3f), "Test setup: the ship should have drifted.");
                Assert.That(Math.Abs((gridNow.Rotation - gridStart.Rotation).Theta), Is.GreaterThan(0.5f), "Test setup: the ship should have turned.");

                Assert.That(xform.GridUid, Is.EqualTo(rig.Grid));
                Assert.That(VehicleRigs.Gap(xform.LocalPosition, start), Is.LessThan(0.001f), "The vehicle slid across the deck.");
                Assert.That(xform.LocalRotation.Theta, Is.EqualTo(startRotation.Theta).Within(0.001f), "The vehicle turned on the deck.");
                Assert.That(VehicleRigs.Gap(VehicleRigs.Mover(rig).Position, start), Is.LessThan(0.001f));
                Assert.That(body.LinearVelocity, Is.EqualTo(Vector2.Zero), "The ship's motion must not become the vehicle's velocity.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Driving gets the vehicle as far along the deck whether or not the deck is going anywhere.</summary>
    [Test]
    public async Task DrivingOnAMovingShipMatchesDrivingAtRest()
    {
        var atRest = Vector2.Zero;
        var moving = Vector2.Zero;
        var shipTravel = 0f;

        await using (var pair = await PoolManager.GetServerClient())
        {
            var rig = await VehicleRigs.Build(pair, maxX: 80);
            await VehicleRigs.Seat(rig);

            var start = Vector2.Zero;
            await pair.Server.WaitAssertion(() => start = VehicleRigs.LocalPosition(rig));
            await pair.Server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
            await pair.Server.WaitRunTicks(120);
            await pair.Server.WaitAssertion(() => atRest = VehicleRigs.LocalPosition(rig) - start);

            await pair.CleanReturnAsync();
        }

        await using (var pair = await PoolManager.GetServerClient())
        {
            var rig = await VehicleRigs.Build(pair, maxX: 80);
            await VehicleRigs.Seat(rig);

            var start = Vector2.Zero;
            var gridStart = Vector2.Zero;
            await pair.Server.WaitAssertion(() =>
            {
                start = VehicleRigs.LocalPosition(rig);
                gridStart = GridPose(rig).Position;
            });

            await SetShipMoving(rig);
            await pair.Server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
            await pair.Server.WaitRunTicks(120);
            await pair.Server.WaitAssertion(() =>
            {
                moving = VehicleRigs.LocalPosition(rig) - start;
                shipTravel = (GridPose(rig).Position - gridStart).Length();
            });

            await pair.CleanReturnAsync();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(atRest.X, Is.GreaterThan(10f), "Test setup: the chassis should have covered some ground.");
            Assert.That(shipTravel, Is.GreaterThan(3f), "Test setup: the ship should have drifted.");
            Assert.That(moving.X, Is.EqualTo(atRest.X).Within(0.05f), "The ship's drift changed how far the vehicle got along the deck.");
            Assert.That(moving.Y, Is.EqualTo(atRest.Y).Within(0.05f), "The ship's spin pushed the vehicle sideways on the deck.");
        }
    }

    /// <summary>
    /// A ship that jumps takes its grid to another map with everything aboard: the operator stays set, the vehicle keeps
    /// its place on the deck, and it still drives when it arrives.
    /// </summary>
    [Test]
    public async Task AShipChangingMapKeepsTheVehicleAndItsDriver()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: 80);
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(40);

        var before = Vector2.Zero;
        var speedBefore = 0f;
        var directionBefore = Vector2i.Zero;
        var newMap = EntityUid.Invalid;
        var newMapId = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            before = VehicleRigs.LocalPosition(rig);
            speedBefore = VehicleRigs.Mover(rig).CurrentSpeed;
            directionBefore = VehicleRigs.Mover(rig).CurrentDirection;
            Assert.That(speedBefore, Is.GreaterThan(1f), "Test setup: the chassis should be rolling.");

            newMap = maps.CreateMap(out newMapId);
            xforms.SetCoordinates(rig.Grid, new EntityCoordinates(newMap, new Vector2(100f, 100f)));
            xforms.SetLocalRotation(rig.Grid, Angle.FromDegrees(30f));

            // As soon as the grid lands, before anything else has run.
            var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);
            var mover = VehicleRigs.Mover(rig);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(newMapId), "The vehicle did not travel with its grid.");
                Assert.That(xform.GridUid, Is.EqualTo(rig.Grid));
                Assert.That(VehicleRigs.Gap(xform.LocalPosition, before), Is.LessThan(0.001f), "The vehicle moved on the deck.");
                Assert.That(mover.SyncedGrid, Is.EqualTo(rig.Grid));
                Assert.That(mover.CurrentSpeed, Is.EqualTo(speedBefore).Within(0.001f), "The jump changed the vehicle's speed.");
                Assert.That(mover.CurrentDirection, Is.EqualTo(directionBefore));
                Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.EqualTo(rig.Driver), "The jump dropped the driver.");
            }
        });

        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);
            var local = xform.LocalPosition;
            var gridPose = GridPose(rig);
            var expectedWorld = gridPose.Position + gridPose.Rotation.RotateVec(local);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(newMapId));
                Assert.That(local.X, Is.GreaterThan(before.X + 5f), "The vehicle stopped driving after the jump.");
                Assert.That(local.Y, Is.EqualTo(before.Y).Within(0.05f), "It should still drive straight along the deck.");
                Assert.That(VehicleRigs.Gap(VehicleRigs.WorldPosition(rig), expectedWorld), Is.LessThan(0.01f), "The vehicle is not where its deck position puts it.");
                Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.EqualTo(rig.Driver));
            }
        });

        // And back again, as a ship does when it arrives.
        var beforeReturn = Vector2.Zero;
        await server.WaitAssertion(() =>
        {
            beforeReturn = VehicleRigs.LocalPosition(rig);
            xforms.SetCoordinates(rig.Grid, new EntityCoordinates(rig.Map.MapUid, Vector2.Zero));
            xforms.SetLocalRotation(rig.Grid, Angle.Zero);

            var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId));
                Assert.That(VehicleRigs.Gap(xform.LocalPosition, beforeReturn), Is.LessThan(0.001f), "The return trip moved the vehicle on the deck.");
                Assert.That(VehicleRigs.Mover(rig).SyncedGrid, Is.EqualTo(rig.Grid));
                Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.EqualTo(rig.Driver));
            }
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(VehicleRigs.LocalPosition(rig).X, Is.GreaterThan(beforeReturn.X + 1f), "The vehicle stopped driving after the ship came back.");
        });

        await pair.CleanReturnAsync();
    }
}
