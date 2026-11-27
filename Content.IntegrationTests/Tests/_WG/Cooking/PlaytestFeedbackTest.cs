using System.Linq;
using Content.Server._WG.Cooking;
using Content.Shared._WG.Cooking;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>Playtest feedback: water in a recipe's bowl, the board's water line, and a kettle left with spent powder.</summary>
[TestFixture]
public sealed class PlaytestFeedbackTest
{
    [Test]
    public async Task BowlOfWaterMakesSoup()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            var containers = entMan.System<SharedContainerSystem>();
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var board = entMan.SpawnEntity("WGPrepBoard", map.GridCoords);

            // The forest mushroom soup's water is poured into the bowl it uses, with no other container.
            var bowl = entMan.SpawnEntity("FoodBowlBig", map.GridCoords);
            Assert.That(solutions.TryGetRefillableSolution(bowl, out var bowlSoln, out _), Is.True);
            solutions.TryAddReagent(bowlSoln!.Value, "Water", FixedPoint2.New(10));
            foreach (var item in new[] { bowl, entMan.SpawnEntity("WGVerdaMushroom", map.GridCoords), entMan.SpawnEntity("WGVerdaMushroom", map.GridCoords) })
            {
                var ev = new InteractUsingEvent(cook, item, board, map.GridCoords);
                entMan.EventBus.RaiseLocalEvent(board, ev);
                Assert.That(ev.Handled, Is.True, $"The board didn't take {entMan.ToPrettyString(item)}.");
            }

            var made = entMan.System<WGCookingSystem>().Assemble((board, entMan.GetComponent<WGPrepBoardComponent>(board)), cook);
            Assert.That(made, Is.Not.Null, "Water in the soup's own bowl didn't count.");
            Assert.That(containers.GetContainer(board, WGPrepBoardComponent.Contents).ContainedEntities, Has.Count.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BoardCountsWaterItHolds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var board = entMan.SpawnEntity("WGPrepBoard", map.GridCoords);
            var beaker = entMan.SpawnEntity("LargeBeaker", map.GridCoords);
            Assert.That(solutions.TryGetRefillableSolution(beaker, out var soln, out _), Is.True);
            solutions.TryAddReagent(soln!.Value, "Water", FixedPoint2.New(50));
            foreach (var item in new[] { beaker, entMan.SpawnEntity("RiceBushel", map.GridCoords), entMan.SpawnEntity("RiceBushel", map.GridCoords) })
                entMan.EventBus.RaiseLocalEvent(board, new InteractUsingEvent(cook, item, board, map.GridCoords));

            Assert.That(entMan.System<UserInterfaceSystem>().TryGetUiState<WGPrepBoardState>(board, WGPrepBoardUiKey.Key, out var state), Is.True);
            var dough = state!.Recipes.Single(r => r.Result == "FoodRiceDough");
            Assert.That(dough.Missing, Has.Count.EqualTo(1), $"Rice dough is missing more than its rice: {string.Join(", ", dough.Missing)}.");
            Assert.That(dough.Have.Any(h => h.Contains("10u")), Is.True, "The board didn't list the water it holds.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CupUsedOnKettleFills()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var kettle = entMan.SpawnEntity("WGCookKettle", map.GridCoords);
            var cup = entMan.SpawnEntity("WGCookCup", map.GridCoords);
            Assert.That(solutions.TryGetSolution(kettle, "pot", out var kettleSoln), Is.True);
            solutions.TryAddReagent(kettleSoln!.Value, "WGTeaBlack", FixedPoint2.New(60));

            var ev = new InteractUsingEvent(cook, cup, kettle, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(kettle, ev);
            Assert.That(ev.Handled, Is.True, "Using a cup on a kettle did nothing.");
            Assert.That(solutions.TryGetRefillableSolution(cup, out _, out var cupSolution), Is.True);
            Assert.That(cupSolution!.GetTotalPrototypeQuantity("WGTeaBlack"), Is.GreaterThan(FixedPoint2.Zero),
                "An empty cup used on a kettle of tea didn't fill.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KettleOfSpentPowderPours()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            var transfer = entMan.System<SolutionTransferSystem>();
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var kettle = entMan.SpawnEntity("WGCookKettle", map.GridCoords);
            var cup = entMan.SpawnEntity("WGCookCup", map.GridCoords);
            Assert.That(solutions.TryGetSolution(kettle, "pot", out var kettleSoln), Is.True);
            Assert.That(solutions.TryGetRefillableSolution(cup, out var cupSoln, out _), Is.True);

            // More powder than the water could take: the water is all tea, and powder is left over.
            solutions.TryAddReagent(kettleSoln!.Value, "WGTeaBlack", FixedPoint2.New(60));
            solutions.TryAddReagent(kettleSoln.Value, "TeaPowder", FixedPoint2.New(20));
            Assert.That(transfer.Transfer(cook, kettle, kettleSoln.Value, cup, cupSoln!.Value, FixedPoint2.New(10)),
                Is.GreaterThan(FixedPoint2.Zero), "A kettle with only spent powder left in it wouldn't pour.");

            // While water is left for it, the powder is still steeping.
            solutions.TryAddReagent(kettleSoln.Value, "Water", FixedPoint2.New(20));
            Assert.That(transfer.Transfer(cook, kettle, kettleSoln.Value, cup, cupSoln.Value, FixedPoint2.New(10)),
                Is.EqualTo(FixedPoint2.Zero), "A kettle poured while its powder was still steeping.");
        });

        await pair.CleanReturnAsync();
    }
}
