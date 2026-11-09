#nullable enable
using System.Numerics;
using Content.Server._WF.ThrowSweep;
using Content.Shared._WF.CCVar;
using Content.Shared.Throwing;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.ThrowSweep;

/// <summary>
/// Something thrown hard at a wall stops at it, at the live physics rate of one step per tick.
/// </summary>
[TestFixture]
[TestOf(typeof(WFThrowSweepSystem))]
public sealed class ThrowSweepTest
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task FastThrowStopsAtTheWall(bool sweep)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var config = server.ResolveDependency<IConfigurationManager>();
        var maps = server.System<SharedMapSystem>();
        var transform = server.System<SharedTransformSystem>();
        var testMap = await pair.CreateTestMap();

        var tickrate = config.GetCVar(CVars.TargetMinimumTickrate);
        var sweepSpeed = config.GetCVar(ThrowSweepCVars.SweepSpeed);
        var item = EntityUid.Invalid;
        var grid = EntityUid.Invalid;

        try
        {
            await server.WaitPost(() =>
            {
                config.SetCVar(CVars.TargetMinimumTickrate, 30);
                config.SetCVar(ThrowSweepCVars.SweepSpeed, sweep ? 6f : 0f);

                var gridEnt = testMap.Grid;
                grid = gridEnt.Owner;
                var floor = testMap.Tile.Tile;
                for (var x = -2; x <= 12; x++)
                for (var y = -2; y <= 2; y++)
                {
                    maps.SetTile(gridEnt, new Vector2i(x, y), floor);
                }

                for (var y = -2; y <= 2; y++)
                {
                    entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 5.5f, y + 0.5f));
                }

                item = entMan.SpawnEntity("Crowbar", new EntityCoordinates(grid, 0.5f, 0.5f));
                server.System<ThrowingSystem>().TryThrow(item, new Vector2(10f, 0f), 100f, playSound: false);
            });

            await server.WaitRunTicks(30);

            await server.WaitAssertion(() =>
            {
                var x = transform.GetWorldPosition(item).X - transform.GetWorldPosition(grid).X;
                if (sweep)
                    Assert.That(x, Is.LessThan(5f), "a fast throw passed into or through the wall");
                else
                    Assert.That(x, Is.GreaterThan(6f), "without the sweep the throw should tunnel; the test no longer reproduces it");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                config.SetCVar(CVars.TargetMinimumTickrate, tickrate);
                config.SetCVar(ThrowSweepCVars.SweepSpeed, sweepSpeed);
            });
        }

        await pair.CleanReturnAsync();
    }
}
