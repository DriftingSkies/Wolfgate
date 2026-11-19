using System.Linq;
using Content.Server._WG.Cooking;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>Cooks on a hearth stove the way a player does: fry, burn, boil a soup, and assemble then bake a cake.</summary>
[TestFixture]
public sealed class HearthCookingTest
{
    [Test]
    public async Task FryBoilAndBake()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var containers = entMan.System<SharedContainerSystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var power = entMan.System<SharedPowerReceiverSystem>();
        var map = await pair.CreateTestMap();

        EntityUid frying = default, stewing = default, board = default, cook = default, pot = default;

        void Use(EntityUid used, EntityUid target)
        {
            var ev = new InteractUsingEvent(cook, used, target, entMan.GetComponent<TransformComponent>(target).Coordinates);
            entMan.EventBus.RaiseLocalEvent(target, ev);
            Assert.That(ev.Handled, Is.True, $"{entMan.ToPrettyString(target)} ignored {entMan.ToPrettyString(used)}.");
        }

        string[] Held(EntityUid owner, string container) =>
            containers.GetContainer(owner, container).ContainedEntities
                .Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID ?? "?").ToArray();

        await server.WaitAssertion(() =>
        {
            cook = entMan.SpawnEntity("WGCookBowl", map.GridCoords);
            frying = entMan.SpawnEntity("WGHearthStove", map.GridCoords);
            stewing = entMan.SpawnEntity("WGHearthStove", map.GridCoords);
            board = entMan.SpawnEntity("WGPrepBoard", map.GridCoords);
            power.SetNeedsPower(frying, false);
            power.SetNeedsPower(stewing, false);

            // A pan on the hob, an egg in the pan.
            var pan = entMan.SpawnEntity("WGCookPan", map.GridCoords);
            Use(pan, frying);
            Use(entMan.SpawnEntity("FoodEgg", map.GridCoords), frying);
            Assert.That(Held(pan, WGPanComponent.Contents), Is.EqualTo(new[] { "FoodEgg" }));

            // A pot of water on the other hob.
            pot = entMan.SpawnEntity("WGCookPot", map.GridCoords);
            Assert.That(solutions.TryGetSolution(pot, "pot", out var soln), Is.True);
            solutions.TryAddReagent(soln!.Value, "Water", FixedPoint2.New(100));
            Use(pot, stewing);

            // Batter and three raspberries on the board make a raw cake.
            Use(entMan.SpawnEntity("FoodCakeBatter", map.GridCoords), board);
            for (var i = 0; i < 3; i++)
                Use(entMan.SpawnEntity("WGFoodRaspberry", map.GridCoords), board);
            Assert.That(Held(board, WGPrepBoardComponent.Contents), Has.Length.EqualTo(4), "The board put things together before being asked.");
            entMan.System<WGCookingSystem>().Assemble((board, entMan.GetComponent<WGPrepBoardComponent>(board)), cook);
            Assert.That(Held(board, WGPrepBoardComponent.Contents), Is.EqualTo(new[] { "WGRawRaspberryCake" }));
        });

        await pair.RunTicksSync(pair.SecondsToTicks(22));
        await server.WaitAssertion(() =>
        {
            var pan = containers.GetContainer(frying, WGHearthComponent.Hob).ContainedEntities[0];
            Assert.That(Held(pan, WGPanComponent.Contents), Is.EqualTo(new[] { "WGFoodFriedEgg" }), "The egg didn't fry.");
            Assert.That(entMan.GetComponent<WGPotComponent>(pot).Boil, Is.GreaterThanOrEqualTo(20f), "The pot didn't boil.");

            // Into the pot goes a potato; the raw cake goes in the frying hearth's oven.
            Use(entMan.SpawnEntity("WGFoodPotatoSliced", map.GridCoords), pot);
            var cake = containers.GetContainer(board, WGPrepBoardComponent.Contents).ContainedEntities[0];
            containers.Remove(cake, containers.GetContainer(board, WGPrepBoardComponent.Contents));
            Use(cake, frying);
            Assert.That(Held(frying, WGHearthComponent.Oven), Is.EqualTo(new[] { "WGRawRaspberryCake" }));
        });

        await pair.RunTicksSync(pair.SecondsToTicks(65));
        await server.WaitAssertion(() =>
        {
            var pan = containers.GetContainer(frying, WGHearthComponent.Hob).ContainedEntities[0];
            Assert.That(Held(pan, WGPanComponent.Contents), Is.EqualTo(new[] { "WGFoodBurnedMess" }), "The egg left on the heat didn't burn.");
            Assert.That(Held(frying, WGHearthComponent.Oven), Is.EqualTo(new[] { "WGFoodRaspberryCake" }), "The cake didn't bake.");
            Assert.That(solutions.TryGetSolution(pot, "pot", out _, out var soup), Is.True);
            Assert.That(soup!.GetTotalPrototypeQuantity("WGSoupPotatoSoup"), Is.EqualTo(FixedPoint2.New(30)), "The potato didn't become soup.");
            Assert.That(soup.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(70)));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChopAndStewTogether()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var containers = entMan.System<SharedContainerSystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var power = entMan.System<SharedPowerReceiverSystem>();
        var map = await pair.CreateTestMap();
        EntityUid cook = default, board = default, hearth = default, pot = default;

        void Use(EntityUid used, EntityUid target)
        {
            var ev = new InteractUsingEvent(cook, used, target, entMan.GetComponent<TransformComponent>(target).Coordinates);
            entMan.EventBus.RaiseLocalEvent(target, ev);
            Assert.That(ev.Handled, Is.True, $"{entMan.ToPrettyString(target)} ignored {entMan.ToPrettyString(used)}.");
        }

        EntityUid TakeFromBoard(string id)
        {
            var contents = containers.GetContainer(board, WGPrepBoardComponent.Contents);
            var item = contents.ContainedEntities.First(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == id);
            containers.Remove(item, contents);
            return item;
        }

        await server.WaitAssertion(() =>
        {
            cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            board = entMan.SpawnEntity("WGPrepBoard", map.GridCoords);
            hearth = entMan.SpawnEntity("KitchenElectricRange", map.GridCoords);
            power.SetNeedsPower(hearth, false);
            var knife = entMan.SpawnEntity("KitchenKnife", map.GridCoords);
            Assert.That(entMan.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickupAnyHand(cook, knife), Is.True);
            var cooking = entMan.System<WGCookingSystem>();
            var boardEnt = (board, entMan.GetComponent<WGPrepBoardComponent>(board));

            // A potato, an onion and some meat, laid on the board and chopped cleanly, as the chopping game reports.
            foreach (var food in new[] { "FoodPotato", "FoodOnion", "FoodMeat" })
            {
                var item = entMan.SpawnEntity(food, map.GridCoords);
                Use(item, board);
                Assert.That(cooking.ChopItem(boardEnt, item, cook, 1f), Is.True, $"{food} didn't chop.");
            }

            pot = entMan.SpawnEntity("WGCookPot", map.GridCoords);
            Assert.That(solutions.TryGetSolution(pot, "pot", out var soln), Is.True);
            solutions.TryAddReagent(soln!.Value, "Water", FixedPoint2.New(200));
            Use(pot, hearth);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(23));
        await server.WaitAssertion(() =>
        {
            foreach (var id in new[] { "WGFoodMeatMince", "WGFoodPotatoSliced", "WGFoodOnionSliced" })
                Use(TakeFromBoard(id), hearth);
            var jobs = entMan.GetComponent<WGPotComponent>(pot).Jobs;
            Assert.That(jobs, Has.Count.EqualTo(1), "The three ingredients didn't combine into one stew.");
            Assert.That(jobs[0].Recipe, Is.EqualTo("WGStewHeartyStew"));
        });

        await pair.RunTicksSync(pair.SecondsToTicks(125));
        await server.WaitAssertion(() =>
        {
            Assert.That(solutions.TryGetSolution(pot, "pot", out _, out var soup), Is.True);
            Assert.That(soup!.GetTotalPrototypeQuantity("WGSoupHeartyStew"), Is.EqualTo(FixedPoint2.New(90)), "The hearty stew didn't cook.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KitchenRecipesArePreparedThenCooked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var containers = entMan.System<SharedContainerSystem>();
        var power = entMan.System<SharedPowerReceiverSystem>();
        var map = await pair.CreateTestMap();
        EntityUid cook = default, board = default, hearth = default;

        string[] Held(EntityUid owner, string container) =>
            containers.GetContainer(owner, container).ContainedEntities
                .Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID ?? "?").ToArray();

        await server.WaitAssertion(() =>
        {
            cook = entMan.SpawnEntity("WGCookBowl", map.GridCoords);
            board = entMan.SpawnEntity("WGPrepBoard", map.GridCoords);
            hearth = entMan.SpawnEntity("KitchenMicrowave", map.GridCoords);
            power.SetNeedsPower(hearth, false);

            // A slice of dough is the kitchen's bun recipe: put together it is an unbaked bun, not a bun.
            var ev = new InteractUsingEvent(cook, entMan.SpawnEntity("FoodDoughSlice", map.GridCoords), board, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(board, ev);
            var unbaked = entMan.System<WGCookingSystem>().Assemble((board, entMan.GetComponent<WGPrepBoardComponent>(board)), cook);
            Assert.That(unbaked, Is.Not.Null, "The board didn't put the dough together.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(unbaked!.Value).EntityPrototype?.ID, Is.EqualTo("WGRawKitchenDish"));

            containers.Remove(unbaked.Value, containers.GetContainer(board, WGPrepBoardComponent.Contents));
            var put = new InteractUsingEvent(cook, unbaked.Value, hearth, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(hearth, put);
            Assert.That(put.Handled, Is.True, "The oven refused the unbaked dish.");
        });

        await pair.RunTicksSync(pair.SecondsToTicks(10));
        await server.WaitAssertion(() =>
            Assert.That(Held(hearth, WGHearthComponent.Oven), Is.EqualTo(new[] { "WGRawKitchenDish" }), "It baked in a blink."));

        await pair.RunTicksSync(pair.SecondsToTicks(20));
        await server.WaitAssertion(() =>
            Assert.That(Held(hearth, WGHearthComponent.Oven), Is.EqualTo(new[] { "FoodBreadBun" }), "The unbaked bun didn't bake."));

        await pair.CleanReturnAsync();
    }
}
