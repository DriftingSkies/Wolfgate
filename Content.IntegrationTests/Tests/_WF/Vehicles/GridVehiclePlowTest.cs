#nullable enable
using System.Collections.Generic;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Damage;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>A plow breaks the walls it is driven front-on into; nothing else about a vehicle does, and a plow breaks nothing but walls.</summary>
[TestFixture]
[TestOf(typeof(VehiclePlowComponent))]
public sealed class GridVehiclePlowTest
{
    private const string Plain = "WFVehicleTestChassis";
    private const string Plow = "WFVehicleTestPlowChassis";
    private const string Grinder = "WFVehicleTestPlowGrinder";

    /// <summary>The column the barrier stands in, and the row the vehicle drives along.</summary>
    private const int BarrierX = 12;
    private const int LaneY = 2;

    /// <summary>Spawns one prototype on every tile of a column across the deck, so the vehicle cannot steer round it, and returns the one in its lane.</summary>
    private static async Task<(EntityUid InLane, List<EntityUid> All)> Barrier(VehicleRig rig, string proto)
    {
        var server = rig.Pair.Server;
        var all = new List<EntityUid>();
        var inLane = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            for (var y = 0; y < 6; y++)
            {
                var uid = server.EntMan.SpawnEntity(proto, new EntityCoordinates(rig.Grid, BarrierX + 0.5f, y + 0.5f));
                all.Add(uid);
                if (y == LaneY)
                    inLane = uid;
            }
        });
        await server.WaitRunTicks(2);

        return (inLane, all);
    }

    private static async Task Drive(VehicleRig rig, int ticks)
    {
        var server = rig.Pair.Server;
        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(ticks);
    }

    /// <summary>At speed, a plow rams a plain wall down in one hit.</summary>
    [Test]
    public async Task APlowRamsAWallDown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: Plow, facingRight: true);
        var (wall, _) = await Barrier(rig, "WallSolid");

        await Drive(rig, 240);

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(pair.Server.EntMan.Deleted(wall), Is.True, "The plow did not break the wall it rammed.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Without a plow the same run leaves the wall untouched and the vehicle in front of it.</summary>
    [Test]
    public async Task WithoutAPlowAWallHolds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: Plain, facingRight: true);
        var (wall, _) = await Barrier(rig, "WallSolid");

        await Drive(rig, 240);

        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.EntMan;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(wall), Is.False, "A vehicle with no plow broke a wall.");
                Assert.That(entMan.GetComponent<DamageableComponent>(wall).TotalDamage.Float(), Is.EqualTo(0f), "A vehicle with no plow damaged a wall.");
                Assert.That(VehicleRigs.LocalPosition(rig).X, Is.LessThan(BarrierX));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A plow breaks walls, not the outposts' walls and not a ship's machines.</summary>
    [Test]
    [TestCase("WallRiveted")]
    [TestCase("Thruster")]
    public async Task APlowLeavesWhatIsNotItsToBreak(string proto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: Grinder, facingRight: true);
        var (_, all) = await Barrier(rig, proto);

        await Drive(rig, 300);

        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.EntMan;
            using (Assert.EnterMultipleScope())
            {
                foreach (var uid in all)
                {
                    Assert.That(entMan.Deleted(uid), Is.False, $"The plow broke a {proto}.");
                    if (!entMan.Deleted(uid))
                        Assert.That(entMan.GetComponent<DamageableComponent>(uid).TotalDamage.Float(), Is.EqualTo(0f), $"The plow damaged a {proto}.");
                }

                Assert.That(VehicleRigs.LocalPosition(rig).X, Is.LessThan(BarrierX));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// From a short run-up a plow only dents a reinforced wall; a plow chassis keeps pushing and grinds it, and the girder
    /// it leaves, away.
    /// </summary>
    [Test]
    [TestCase(Plow, false)]
    [TestCase(Grinder, true)]
    public async Task OnlyAPlowChassisGrindsThroughFromAStandstill(string proto, bool through)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: proto, facingRight: true, startX: 9.5f);
        var (wall, _) = await Barrier(rig, "WallReinforced");

        await Drive(rig, 300);

        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.EntMan;
            using (Assert.EnterMultipleScope())
            {
                if (through)
                {
                    Assert.That(entMan.Deleted(wall), Is.True, "The plow chassis did not grind the wall down.");
                    Assert.That(VehicleRigs.LocalPosition(rig).X, Is.GreaterThan(BarrierX + 1f), "It did not drive on through the gap.");
                }
                else
                {
                    Assert.That(entMan.Deleted(wall), Is.False, "A plow with no run-up broke a reinforced wall.");
                    if (!entMan.Deleted(wall))
                        Assert.That(entMan.GetComponent<DamageableComponent>(wall).TotalDamage.Float(), Is.GreaterThan(0f), "The ram should have dented the wall.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
