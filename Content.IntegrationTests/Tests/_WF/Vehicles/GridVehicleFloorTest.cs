#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>The floor under a vehicle: it stops at a deck edge, can back off one it overhangs, and drives from one grid onto the next.</summary>
[TestFixture]
public sealed class GridVehicleFloorTest
{
    /// <summary>The first tile past the end of the deck.</summary>
    private const int EdgeX = 20;

    /// <summary>The chassis is 2.4 long, so its front sits this far ahead of its origin when it faces along the grid.</summary>
    private const float HalfLength = 1.2f;

    private static IEnumerable<Vector2i> Tiles(int minX, int maxX, int minY, int maxY)
    {
        for (var x = minX; x < maxX; x++)
        {
            for (var y = minY; y < maxY; y++)
            {
                yield return new Vector2i(x, y);
            }
        }
    }

    /// <summary>A vehicle driven at the end of the floor stops short of it, stays on the grid and reports a crash.</summary>
    [Test]
    public async Task DrivingOffADeckEdgeIsRefused()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: EdgeX);
        var server = pair.Server;
        var probe = server.System<VehicleEventProbeSystem>();

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var position = VehicleRigs.LocalPosition(rig);
            var xform = server.EntMan.GetComponent<TransformComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(position.X + HalfLength, Is.LessThan(EdgeX + 0.1f), "The chassis drove off the deck.");
                Assert.That(position.X + HalfLength, Is.GreaterThan(EdgeX - 0.7f), "The chassis stopped well short of the edge.");
                Assert.That(xform.GridUid, Is.EqualTo(rig.Grid), "The chassis left the grid.");
                Assert.That(VehicleRigs.Mover(rig).CurrentSpeed, Is.Zero, "A refused chassis should have no speed.");
                Assert.That(
                    probe.Collisions.Any(c => c.Vehicle == rig.Vehicle && c.Event.Kind == VehicleCollisionKind.Crash && c.Event.Speed > 1f),
                    Is.True,
                    "Stopping at the edge should be reported as a crash.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A vehicle already hanging over the edge may move to where it hangs over less, and no further out.</summary>
    [Test]
    public async Task AnOverhangingVehicleCanBackOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: EdgeX, startX: 19.3f, facingRight: true);
        var server = pair.Server;

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Left));
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            var position = VehicleRigs.LocalPosition(rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(position.X, Is.LessThan(16f), "The overhanging chassis could not back away from the edge.");
                Assert.That(position.X + HalfLength, Is.LessThanOrEqualTo(EdgeX), "It should be back on the deck.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnOverhangingVehicleCannotGoFurtherOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: EdgeX, startX: 19.3f, facingRight: true);
        var server = pair.Server;

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(VehicleRigs.LocalPosition(rig).X, Is.EqualTo(19.3f).Within(0.12f), "The overhanging chassis crept further out.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hole in the deck one tile wide, with the vehicle's origin just short of it and its 2.4 long hull already over all
    /// of it. Driving on moves the origin over the hole without the hull covering any more of it than it does where it
    /// stands, so the move is allowed; the vehicle must stay a child of its deck while its origin is over the hole, where
    /// the engine's grid traversal would take it to the map at the first step and leave it behind with the ship gone.
    /// </summary>
    [Test]
    public async Task AVehicleWhoseOriginCrossesAHoleStaysOnItsDeck()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, startX: 9.9f, facingRight: true);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => server.System<SharedMapSystem>().SetTile(rig.Grid, rig.Map.Grid.Comp, new Vector2i(10, 2), Tile.Empty));
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(rig.Vehicle).GridUid, Is.EqualTo(rig.Grid), "Test setup: the vehicle should still be on the deck.");
        });

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));

        var overTheHole = false;

        for (var i = 0; i < 40; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);
                overTheHole |= xform.LocalPosition.X is > 10.05f and < 10.95f;
                Assert.That(xform.GridUid, Is.EqualTo(rig.Grid), $"Tick {i}: the vehicle left its deck at x {xform.LocalPosition.X:0.00}.");
            });
        }

        await server.WaitRunTicks(110);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(overTheHole, Is.True, "Test setup: the vehicle's origin should have been seen over the hole.");
                Assert.That(xform.GridUid, Is.EqualTo(rig.Grid));
                Assert.That(VehicleRigs.Mover(rig).SyncedGrid, Is.EqualTo(rig.Grid));
                Assert.That(xform.LocalPosition.X, Is.GreaterThan(14f), "The vehicle did not drive on past the hole.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Nothing hands a vehicle to the floor under it any more but the mover, so one set down on the map over a deck joins it.</summary>
    [Test]
    public async Task AVehicleOnTheMapOverADeckIsPickedUpByIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, startX: 10.5f);
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var world = Vector2.Zero;

        await server.WaitPost(() =>
        {
            world = xforms.GetWorldPosition(rig.Vehicle);
            xforms.SetParent(rig.Vehicle, rig.Map.MapUid);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(rig.Vehicle).GridUid, Is.Null, "Test setup: the vehicle should be on the map.");
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.GridUid, Is.EqualTo(rig.Grid), "The deck under the vehicle did not take it up.");
                Assert.That(VehicleRigs.Mover(rig).SyncedGrid, Is.EqualTo(rig.Grid));
                Assert.That(VehicleRigs.Gap(xforms.GetWorldPosition(rig.Vehicle), world), Is.LessThan(0.001f));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A handler of the open tile event decides, so the falling system can let a vehicle off the edge.</summary>
    [Test]
    public async Task AHandlerCanOpenTheEdge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: EdgeX);
        var server = pair.Server;
        var probe = server.System<VehicleEventProbeSystem>();

        await VehicleRigs.Seat(rig);

        try
        {
            await server.WaitPost(() =>
            {
                probe.AllowOpenTiles = true;
                VehicleRigs.Hold(rig, MoveButtons.Right);
            });
            await server.WaitRunTicks(300);

            await server.WaitAssertion(() =>
            {
                var xform = server.EntMan.GetComponent<TransformComponent>(rig.Vehicle);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(VehicleRigs.WorldPosition(rig).X, Is.GreaterThan(EdgeX), "The handler allowed the tile, so the chassis should have driven off.");
                    Assert.That(xform.GridUid, Is.Null, "A chassis past the edge is no longer on the grid.");
                    Assert.That(
                        probe.OpenTileAttempts.Any(a => a.Vehicle == rig.Vehicle && a.Event.Grid == rig.Grid && a.Event.Tile.X == EdgeX),
                        Is.True,
                        "The event should name the grid and the empty tile.");
                }
            });
        }
        finally
        {
            // The pair goes back to the pool as it is, so the next test must not find every empty tile enterable.
            await server.WaitPost(probe.Reset);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Another grid's floor counts as floor, and the vehicle keeps its speed across the seam.</summary>
    [Test]
    public async Task DrivingOntoAnotherGridKeepsTheSpeed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: EdgeX);
        var server = pair.Server;
        var other = await VehicleRigs.AddGrid(rig, new Vector2(EdgeX, 0), 0f, Tiles(0, 40, 0, 6));

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));

        var topSpeed = false;
        var crossed = false;
        var minSpeed = float.MaxValue;

        for (var i = 0; i < 300; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var speed = VehicleRigs.Mover(rig).CurrentSpeed;
                var x = VehicleRigs.WorldPosition(rig).X;

                if (speed > 5.5f)
                    topSpeed = true;

                if (topSpeed && x > 10f && x < 26f)
                    minSpeed = MathF.Min(minSpeed, speed);

                if (server.EntMan.GetComponent<TransformComponent>(rig.Vehicle).GridUid == other)
                    crossed = true;
            });
        }

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(topSpeed, Is.True, "Test setup: the chassis never reached top speed.");
                Assert.That(crossed, Is.True, "The chassis never moved onto the second grid.");
                Assert.That(minSpeed, Is.GreaterThan(5.5f), "The chassis lost speed crossing the seam.");
                Assert.That(VehicleRigs.WorldPosition(rig).X, Is.GreaterThan(EdgeX + 10f));
                Assert.That(VehicleRigs.WorldPosition(rig).Y, Is.EqualTo(2.5f).Within(0.1f), "It should have kept heading the same way.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A second grid turned a quarter turn: the vehicle's facing is read again in the new frame, so it carries on in the
    /// same direction in the world, with the speed it had.
    /// </summary>
    [Test]
    public async Task ChangingToATurnedGridKeepsTheHeading()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: EdgeX);
        var server = pair.Server;
        var xforms = server.System<SharedTransformSystem>();

        // Turned 90 degrees about its origin, local (u, v) lands at (20 - v, u): this floor covers world x in [20, 60), y in [0, 6).
        var other = await VehicleRigs.AddGrid(rig, new Vector2(EdgeX, 0), 90f, Tiles(0, 6, -40, 0));

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));

        var arrived = false;
        for (var i = 0; i < 300 && !arrived; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() => arrived = VehicleRigs.WorldPosition(rig).X >= 19f);
        }

        // Let go so the held key, which is relative to the grid, does not ask for a turn on the new grid.
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.None));

        var seen = false;
        var speedOnArrival = 0f;
        var direction = Vector2i.Zero;
        var worldHeading = Vector2.Zero;
        var lastX = 0f;
        var drift = 0f;
        await server.WaitAssertion(() => lastX = VehicleRigs.WorldPosition(rig).X);

        for (var i = 0; i < 60; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var onOther = server.EntMan.GetComponent<TransformComponent>(rig.Vehicle).GridUid == other;
                var world = VehicleRigs.WorldPosition(rig);
                drift = MathF.Max(drift, MathF.Abs(world.Y - 2.5f));

                if (!onOther || seen)
                {
                    if (seen)
                        Assert.That(world.X, Is.GreaterThanOrEqualTo(lastX - 0.001f), "The chassis turned back.");

                    lastX = world.X;
                    return;
                }

                seen = true;
                var mover = VehicleRigs.Mover(rig);
                speedOnArrival = mover.CurrentSpeed;
                direction = mover.CurrentDirection;
                worldHeading = xforms.GetWorldRotation(rig.Vehicle).ToWorldVec();
                lastX = world.X;
            });
        }

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(seen, Is.True, "The chassis never moved onto the turned grid.");
                Assert.That(speedOnArrival, Is.GreaterThan(0.5f), "The chassis lost its speed changing grid.");
                Assert.That(direction, Is.EqualTo(new Vector2i(0, -1)), "East in the world is minus V on a grid turned a quarter turn.");
                Assert.That(worldHeading.X, Is.EqualTo(1f).Within(0.001f), "The chassis should still face east.");
                Assert.That(worldHeading.Y, Is.EqualTo(0f).Within(0.001f));
                Assert.That(drift, Is.LessThan(0.1f), "The chassis wandered off its line.");
                Assert.That(VehicleRigs.WorldPosition(rig).X, Is.GreaterThan(EdgeX + 0.1f), "The chassis should have rolled on past the seam.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The same seam with the key still held. The direction keys are read in the frame of the grid the vehicle is on, as
    /// the controls are relative to the deck, so the key that meant east on the first deck means the new deck's own
    /// right, which is north in the world here. Past the seam the held key asks for a turn: the heading is kept only
    /// while the key is let go, as the test above does. This pins that behaviour down so a change to it is deliberate.
    /// </summary>
    [Test]
    public async Task AKeyHeldAcrossATurnedSeamIsReadInTheNewGridsFrame()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: EdgeX);
        var server = pair.Server;
        var xforms = server.System<SharedTransformSystem>();

        // The floor covers world x in [20, 60), y in [0, 6); its own +U is north in the world.
        var other = await VehicleRigs.AddGrid(rig, new Vector2(EdgeX, 0), 90f, Tiles(0, 6, -40, 0));

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(240);

        await server.WaitAssertion(() =>
        {
            var xform = server.EntMan.GetComponent<TransformComponent>(rig.Vehicle);
            var mover = VehicleRigs.Mover(rig);
            var heading = xforms.GetWorldRotation(rig.Vehicle).ToWorldVec();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.GridUid, Is.EqualTo(other), "Test setup: the chassis should have crossed to the second grid.");
                Assert.That(mover.CurrentDirection, Is.EqualTo(new Vector2i(1, 0)), "The held key should be read in the new grid's frame.");
                Assert.That(heading.X, Is.EqualTo(0f).Within(0.001f), "The chassis should have turned to the new deck's right, which is north.");
                Assert.That(heading.Y, Is.EqualTo(1f).Within(0.001f));
                Assert.That(VehicleRigs.WorldPosition(rig).Y, Is.GreaterThan(3.5f), "It should have driven north along the second deck.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
