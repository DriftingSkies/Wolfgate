#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Car-style steering of the grid mover: throttle along the heading, a turn rate of its own and no snapping.</summary>
[TestFixture]
[TestOf(typeof(GridVehicleMoverComponent))]
public sealed class GridVehicleSteeringTest
{
    private const string Wheeled = "WFVehicleTestSteeredChassis";
    private const string Tracked = "WFVehicleTestSteeredTracked";
    private static readonly Angle East = Angle.FromWorldVec(Vector2.UnitX);

    private static double DegreesFrom(Angle from, Angle to)
    {
        return Angle.ShortestDistance(from, to).Degrees;
    }

    /// <summary>The throttle rolls the vehicle along the heading it was left with, and nothing turns it.</summary>
    [Test]
    public async Task ThrottleDrivesAlongTheHeading()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: Wheeled, facingRight: true);
        var server = pair.Server;

        await VehicleRigs.Seat(rig);
        var start = VehicleRigs.LocalPosition(rig);

        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Up));
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            var position = VehicleRigs.LocalPosition(rig);
            var mover = VehicleRigs.Mover(rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(position.X, Is.GreaterThan(start.X + 3f), "The vehicle barely moved with the throttle held.");
                Assert.That(position.Y, Is.EqualTo(start.Y).Within(0.05f), "It should roll straight along its heading.");
                Assert.That(DegreesFrom(East, mover.Heading), Is.EqualTo(0).Within(0.01), "The heading changed without steering.");
                Assert.That(mover.CurrentDirection, Is.EqualTo(new Vector2i(1, 0)));
                Assert.That(mover.CurrentSpeed, Is.GreaterThan(0f));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Steering while rolling turns the chassis a little each step, so the vehicle drives an arc.</summary>
    [Test]
    public async Task SteeringTurnsInAnArc()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: Wheeled, facingRight: true, maxY: 12, startY: 3.5f);
        var server = pair.Server;

        await VehicleRigs.Seat(rig);
        var start = VehicleRigs.LocalPosition(rig);

        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Up | MoveButtons.Left));
        await server.WaitRunTicks(6);

        var early = 0d;
        await server.WaitAssertion(() =>
        {
            early = DegreesFrom(East, VehicleRigs.Mover(rig).Heading);
            Assert.That(early, Is.GreaterThan(0).And.LessThan(45), "The first steps of a turn should be a small anticlockwise angle.");
        });

        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            var mover = VehicleRigs.Mover(rig);
            var turned = DegreesFrom(East, mover.Heading);
            var position = VehicleRigs.LocalPosition(rig);
            var xform = server.EntMan.GetComponent<TransformComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(turned, Is.GreaterThan(early).And.LessThan(90), "The turn should keep growing, short of a quarter turn.");
                Assert.That(position.X, Is.GreaterThan(start.X), "It should still be travelling forwards.");
                Assert.That(position.Y, Is.GreaterThan(start.Y), "Turning left from east curves towards north.");
                Assert.That(DegreesFrom(mover.Heading, xform.LocalRotation), Is.EqualTo(0).Within(0.01), "The transform does not show the heading.");
                Assert.That(MathF.Abs(mover.AngularVelocityDegrees), Is.LessThanOrEqualTo(mover.MaxRotationSpeedDegrees + 0.001f));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Wheels cannot turn a vehicle that stands still; tracks can, and it stays where it is.</summary>
    [Test]
    [TestCase(Wheeled, false)]
    [TestCase(Tracked, true)]
    public async Task OnlyTracksTurnOnTheSpot(string proto, bool turns)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: proto, facingRight: true);
        var server = pair.Server;

        await VehicleRigs.Seat(rig);
        var start = VehicleRigs.LocalPosition(rig);

        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Left));
        await server.WaitRunTicks(20);

        await server.WaitAssertion(() =>
        {
            var mover = VehicleRigs.Mover(rig);
            var xform = server.EntMan.GetComponent<TransformComponent>(rig.Vehicle);
            var turned = DegreesFrom(East, xform.LocalRotation);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(VehicleRigs.Gap(VehicleRigs.LocalPosition(rig), start), Is.LessThan(0.01f), "Steering alone moved the vehicle.");
                if (turns)
                {
                    Assert.That(turned, Is.GreaterThan(5), "A tracked vehicle should turn on the spot.");
                    Assert.That(mover.IsMoving, Is.True, "A vehicle turning on the spot counts as moving.");
                }
                else
                {
                    Assert.That(turned, Is.EqualTo(0).Within(0.01), "A wheeled vehicle turned while standing still.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>In reverse the same steering key swings the chassis the other way, as a car's does.</summary>
    [Test]
    public async Task SteeringIsMirroredInReverse()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: Wheeled, facingRight: true, maxY: 12, startX: 20.5f, startY: 6.5f);
        var server = pair.Server;

        await VehicleRigs.Seat(rig);
        var start = VehicleRigs.LocalPosition(rig);

        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Down | MoveButtons.Left));
        await server.WaitRunTicks(16);

        await server.WaitAssertion(() =>
        {
            var mover = VehicleRigs.Mover(rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(VehicleRigs.LocalPosition(rig).X, Is.LessThan(start.X), "It should be rolling backwards.");
                Assert.That(DegreesFrom(East, mover.Heading), Is.LessThan(0).And.GreaterThan(-90), "Reversing should mirror the steering.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The heading is kept in the deck's own frame, so a turned deck changes nothing about how the vehicle drives on it.</summary>
    [Test]
    public async Task HeadingIsInTheGridsFrame()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: Wheeled, facingRight: true, degrees: 30f);
        var server = pair.Server;

        await VehicleRigs.Seat(rig);
        var start = VehicleRigs.LocalPosition(rig);

        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Up));
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            var position = VehicleRigs.LocalPosition(rig);
            var mover = VehicleRigs.Mover(rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(position.X, Is.GreaterThan(start.X + 3f));
                Assert.That(position.Y, Is.EqualTo(start.Y).Within(0.05f), "On a turned deck it should still roll along the deck's own axis.");
                Assert.That(DegreesFrom(East, mover.Heading), Is.EqualTo(0).Within(0.01));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A turn that would swing the hull into a wall does not happen, and the vehicle keeps its heading.</summary>
    [Test]
    public async Task AWallStopsTheTurn()
    {
        await using var pair = await PoolManager.GetServerClient();

        // Facing east in a corridor one tile high, walled above and below along its whole length.
        var walls = new List<Vector2i>();
        for (var x = 0; x <= 6; x++)
        {
            walls.Add(new Vector2i(x, 1));
            walls.Add(new Vector2i(x, 3));
        }

        var rig = await VehicleRigs.Build(pair, proto: Tracked, facingRight: true, startX: 3.5f, startY: 2.5f, walls: walls);
        var server = pair.Server;

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Left));
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var xform = server.EntMan.GetComponent<TransformComponent>(rig.Vehicle);
            Assert.That(DegreesFrom(East, xform.LocalRotation), Is.EqualTo(0).Within(8), "The hull turned through the corridor walls.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull at 45 degrees is tested as it lies, not as the upright box round it: a wall that only stands in a free
    /// corner of that box does not stop it.
    /// </summary>
    [Test]
    public async Task ADiagonalHullIsNotStoppedByItsBoundingBox()
    {
        await using var pair = await PoolManager.GetServerClient();

        // The hull points down and to the right from (10.5, 6.5); this tile is up and to the right, inside the box round the hull.
        var walls = new List<Vector2i> { new(11, 7) };
        var rig = await VehicleRigs.Build(pair, proto: Wheeled, maxY: 12, startX: 10.5f, startY: 6.5f, walls: walls);
        var server = pair.Server;

        await server.WaitPost(() => server.System<SharedTransformSystem>().SetLocalRotation(rig.Vehicle, Angle.FromDegrees(45)));
        await VehicleRigs.Seat(rig);
        var start = VehicleRigs.LocalPosition(rig);

        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Up));
        await server.WaitRunTicks(20);

        await server.WaitAssertion(() =>
        {
            var moved = VehicleRigs.LocalPosition(rig) - start;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(moved.Length(), Is.GreaterThan(0.5f), "The wall in the corner of its bounding box stopped the hull.");
                Assert.That(moved.X, Is.GreaterThan(0f));
                Assert.That(moved.Y, Is.LessThan(0f), "It should roll along its heading, down and to the right.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Driven into a wall at a shallow angle the hull slides along it and keeps most of its speed; it is not stopped dead.</summary>
    [Test]
    public async Task AGlancingContactSlidesAlongTheWall()
    {
        await using var pair = await PoolManager.GetServerClient();

        var walls = new List<Vector2i>();
        for (var x = 0; x < 40; x++)
        {
            walls.Add(new Vector2i(x, 4));
        }

        var rig = await VehicleRigs.Build(pair, proto: Wheeled, facingRight: true, walls: walls);
        var server = pair.Server;

        // East, turned twenty degrees towards the wall above.
        await server.WaitPost(() => server.System<SharedTransformSystem>().SetLocalRotation(rig.Vehicle, East + Angle.FromDegrees(20)));
        await VehicleRigs.Seat(rig);
        var start = VehicleRigs.LocalPosition(rig);

        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Up));
        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var position = VehicleRigs.LocalPosition(rig);
            var mover = VehicleRigs.Mover(rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(position.X, Is.GreaterThan(start.X + 8f), "The wall stopped the hull instead of letting it slide along.");
                Assert.That(position.Y, Is.LessThan(4f), "The hull went into the wall.");
                Assert.That(mover.CurrentSpeed, Is.GreaterThan(2f), "Sliding along a wall at a shallow angle should keep most of the speed.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The driver is given the steering switch. To the four-way drive it squares the hull to the nearest direction and the
    /// keys turn the vehicle to face them; back to free steering the vehicle keeps the heading it has.
    /// </summary>
    [Test]
    public async Task TheDriverSwitchesBetweenTheTwoDrives()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: Wheeled, facingRight: true, maxY: 12, startX: 10.5f, startY: 6.5f);
        var server = pair.Server;
        var movers = server.System<GridVehicleMoverSystem>();

        await server.WaitPost(() => server.System<SharedTransformSystem>().SetLocalRotation(rig.Vehicle, East + Angle.FromDegrees(20)));
        await VehicleRigs.Seat(rig);

        // A touch of throttle gives it its heading.
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Up));
        await server.WaitRunTicks(3);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.None));
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var mover = VehicleRigs.Mover(rig);
            var xform = server.EntMan.GetComponent<TransformComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mover.SteeringAction, Is.Not.Null, "The driver was not given the steering switch.");
                Assert.That(movers.TrySetFreeSteering((rig.Vehicle, mover), false), Is.True, "It could not switch to the four-way drive in the open.");
                Assert.That(mover.FreeSteering, Is.False);
                Assert.That(mover.CurrentDirection, Is.EqualTo(new Vector2i(1, 0)));
                Assert.That(DegreesFrom(East, xform.LocalRotation), Is.EqualTo(0).Within(0.01), "The hull was not squared to the nearest direction.");
            }
        });

        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Up));
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var mover = VehicleRigs.Mover(rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mover.CurrentDirection, Is.EqualTo(new Vector2i(0, 1)), "On the four-way drive the up key should turn it to face up.");
                Assert.That(movers.TrySetFreeSteering((rig.Vehicle, mover), true), Is.True);
                Assert.That(mover.FreeSteering, Is.True);
                Assert.That(DegreesFrom(Angle.FromWorldVec(Vector2.UnitY), mover.Heading), Is.EqualTo(0).Within(0.01), "Free steering should start from the heading it had.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
