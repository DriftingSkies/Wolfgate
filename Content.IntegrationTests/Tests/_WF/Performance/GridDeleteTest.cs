#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Performance;
using Content.Shared._WF.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>
/// A grid queued with the grid deleter leaves its map the same tick, its contents go over the ticks after under the
/// budget, and the grid itself goes last.
/// </summary>
[TestFixture]
[TestOf(typeof(WFGridDeleteSystem))]
public sealed class GridDeleteTest
{
    [Test]
    public async Task GridLeavesAtOnceAndEmptiesOverTicks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var deleter = server.System<WFGridDeleteSystem>();
        var testMap = await pair.CreateTestMap();

        var grid = EntityUid.Invalid;
        var children = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            // A budget so small that a tick never empties the grid, to see the slicing.
            server.CfgMan.SetCVar(PerformanceCVars.GridDeleteBudgetMs, 0.001f);

            var gridEnt = maps.CreateGridEntity(testMap.MapId);
            grid = gridEnt.Owner;
            for (var x = 0; x < 8; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    maps.SetTile(grid, gridEnt.Comp, new Vector2i(x, y), new Tile(1));
                    children.Add(entMan.SpawnEntity(null, new EntityCoordinates(grid, x + 0.5f, y + 0.5f)));
                }
            }

            deleter.QueueDel(grid);

            Assert.That(entMan.EntityExists(grid), "the grid itself must outlive the queueing tick");
            Assert.That(entMan.GetComponent<TransformComponent>(grid).MapUid, Is.Not.EqualTo(testMap.MapUid),
                "the grid should leave its map at once");
            Assert.That(deleter.Pending, Is.EqualTo(1));
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            var alive = children.FindAll(entMan.EntityExists).Count;
            Assert.That(alive, Is.GreaterThan(0), "a tiny budget should leave children for later ticks");
            Assert.That(alive, Is.LessThan(children.Count), "the first tick should still delete some children");
            Assert.That(entMan.EntityExists(grid), "the grid goes only once it is empty");
        });

        await server.WaitRunTicks(120);
        await server.WaitAssertion(() =>
        {
            Assert.That(children.FindAll(entMan.EntityExists), Is.Empty, "children left after the budget had 120 ticks");
            Assert.That(entMan.EntityExists(grid), Is.False, "the emptied grid was not deleted");
            Assert.That(deleter.Pending, Is.EqualTo(0));
            server.CfgMan.SetCVar(PerformanceCVars.GridDeleteBudgetMs, PerformanceCVars.GridDeleteBudgetMs.DefaultValue);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ZeroBudgetDeletesAtOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var deleter = server.System<WFGridDeleteSystem>();
        var testMap = await pair.CreateTestMap();

        var grid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PerformanceCVars.GridDeleteBudgetMs, 0f);
            var gridEnt = maps.CreateGridEntity(testMap.MapId);
            grid = gridEnt.Owner;
            maps.SetTile(grid, gridEnt.Comp, Vector2i.Zero, new Tile(1));
            entMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            deleter.QueueDel(grid);
            Assert.That(deleter.Pending, Is.EqualTo(0), "with no budget the engine's own queued deletion is used");
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(grid), Is.False);
            server.CfgMan.SetCVar(PerformanceCVars.GridDeleteBudgetMs, PerformanceCVars.GridDeleteBudgetMs.DefaultValue);
        });

        await pair.CleanReturnAsync();
    }
}
