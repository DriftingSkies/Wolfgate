using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Cooking;
using Content.Server._WG.Farming;
using Content.Server.Botany.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared._WG.Farming;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Server.Nutrition.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>Regression tests for the kitchen and farm review: each pins one bug that was found and fixed.</summary>
[TestFixture]
public sealed class KitchenFixesTest
{
    [Test]
    public async Task FeedingSomeoneGivesThemTheBuff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid feeder = default, eater = default, food = default;

        await server.WaitAssertion(() =>
        {
            feeder = entMan.SpawnEntity("MobHuman", map.GridCoords);
            eater = entMan.SpawnEntity("MobHuman", map.GridCoords);
            food = entMan.SpawnEntity("WGFoodFriedEgg", map.GridCoords);
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(feeder, food), Is.True);
        });

        // Feeding someone else is a bite at a time.
        for (var i = 0; i < 15 && !entMan.Deleted(food); i++)
        {
            await server.WaitPost(() =>
            {
                if (!entMan.Deleted(food))
                    entMan.System<FoodSystem>().TryFeed(feeder, eater, food, entMan.GetComponent<FoodComponent>(food));
            });
            await pair.RunTicksSync(pair.SecondsToTicks(6));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(food), Is.True, "The egg was never finished.");
            Assert.That(entMan.HasComponent<WGWellFedComponent>(eater), Is.True, "The one fed got no buff.");
            Assert.That(entMan.HasComponent<WGWellFedComponent>(feeder), Is.False, "The feeder got the buff.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task QualityCarriesThroughCookingAndBurnedMessStays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var containers = entMan.System<SharedContainerSystem>();
        var map = await pair.CreateTestMap();
        EntityUid pan = default, mess = default;

        await server.WaitAssertion(() =>
        {
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var hearth = entMan.SpawnEntity("WGHearthStove", map.GridCoords);
            entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(hearth, false);
            pan = entMan.SpawnEntity("WGCookPan", map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(hearth, new InteractUsingEvent(cook, pan, hearth, map.GridCoords));

            var egg = entMan.SpawnEntity("FoodEgg", map.GridCoords);
            entMan.AddComponent<WGProduceQualityComponent>(egg).Quality = WGCropQuality.Gold;
            entMan.EventBus.RaiseLocalEvent(hearth, new InteractUsingEvent(cook, egg, hearth, map.GridCoords));
            // The mess doesn't fry, so it goes in the oven, where it sits on the heat all the same.
            mess = entMan.SpawnEntity("WGFoodBurnedMess", map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(hearth, new InteractUsingEvent(cook, mess, hearth, map.GridCoords));
            Assert.That(containers.GetContainer(pan, WGPanComponent.Contents).ContainedEntities, Has.Count.EqualTo(1));
            Assert.That(containers.GetContainer(hearth, WGHearthComponent.Oven).ContainedEntities, Does.Contain(mess));
        });

        // Long enough to fry the egg, not to burn it; the mess sits a minute and more on the heat.
        await pair.RunTicksSync(pair.SecondsToTicks(25));
        await server.WaitAssertion(() =>
        {
            var fried = containers.GetContainer(pan, WGPanComponent.Contents).ContainedEntities
                .First(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WGFoodFriedEgg");
            Assert.That(entMan.GetComponent<WGProduceQualityComponent>(fried).Quality, Is.EqualTo(WGCropQuality.Gold), "The gold egg fried into a plain one.");
        });

        await pair.RunTicksSync(pair.SecondsToTicks(70));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(mess), Is.False, "The burned mess burned again.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BoardOffersEveryCutAndLadlesFromTheHob()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var containers = entMan.System<SharedContainerSystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cooking = entMan.System<WGCookingSystem>();
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(cook, entMan.SpawnEntity("KitchenKnife", map.GridCoords)), Is.True);
            var board = entMan.SpawnEntity("WGPrepBoard", map.GridCoords);

            // Meat chops into our mince or the game's cutlets, which 11 kitchen recipes need.
            var meat = entMan.SpawnEntity("FoodMeat", map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(board, new InteractUsingEvent(cook, meat, board, map.GridCoords));
            var options = cooking.ChopOptions(meat);
            Assert.That(options.Select(o => o.Result.Id), Is.EqualTo(new[] { "WGFoodMeatMince", "FoodMeatCutlet" }));
            Assert.That(cooking.ChopItem((board, entMan.GetComponent<WGPrepBoardComponent>(board)), meat, cook, 1f, option: 1), Is.True);
            var onBoard = containers.GetContainer(board, WGPrepBoardComponent.Contents).ContainedEntities
                .Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID).ToList();
            Assert.That(onBoard, Is.All.EqualTo("FoodMeatCutlet"));
            Assert.That(onBoard, Has.Count.EqualTo(options[1].Count));

            // A pot isn't laid on the board.
            var spare = entMan.SpawnEntity("WGCookPot", map.GridCoords);
            var put = new InteractUsingEvent(cook, spare, board, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(board, put);
            Assert.That(put.Handled, Is.False, "The board took a pot.");

            // A bowl used on the hearth ladles soup, not water, out of the pot on the hob.
            var hearth = entMan.SpawnEntity("WGHearthStove", map.GridCoords);
            var pot = entMan.SpawnEntity("WGCookPot", map.GridCoords);
            Assert.That(solutions.TryGetSolution(pot, "pot", out var potSoln), Is.True);
            solutions.TryAddReagent(potSoln!.Value, "Water", FixedPoint2.New(100));
            solutions.TryAddReagent(potSoln.Value, "WGSoupPotatoSoup", FixedPoint2.New(30));
            entMan.EventBus.RaiseLocalEvent(hearth, new InteractUsingEvent(cook, pot, hearth, map.GridCoords));
            var bowl = entMan.SpawnEntity("WGCookBowl", map.GridCoords);
            var ladle = new InteractUsingEvent(cook, bowl, hearth, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(hearth, ladle);
            Assert.That(ladle.Handled, Is.True, "The hearth didn't pass the bowl to the pot.");
            Assert.That(solutions.TryGetDrainableSolution(bowl, out _, out var inBowl), Is.True);
            Assert.That(inBowl!.Volume, Is.GreaterThan(FixedPoint2.Zero), "Nothing was ladled.");
            Assert.That(inBowl.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.Zero), "The bowl came up with water while there was soup.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FarmFixes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid tray = default, board = default, farmer = default;

        await server.WaitAssertion(() =>
        {
            farmer = entMan.SpawnEntity("MobHuman", map.GridCoords);

            // Fertilizer laid in empty soil before planting stays there.
            tray = entMan.SpawnEntity("HydroponicsTrayEmpty", map.GridCoords);
            var fertilizer = entMan.SpawnEntity("WGFertilizerQuality", map.GridCoords);
            var use = new AfterInteractEvent(farmer, fertilizer, tray, map.GridCoords, true);
            entMan.EventBus.RaiseLocalEvent(fertilizer, use);
            Assert.That(entMan.GetComponent<WGFarmPlotComponent>(tray).FertQuality, Is.GreaterThan(0));

            // The seed maker takes plain produce, which names its seed only by prototype.
            var maker = entMan.SpawnEntity("WGFarmSeedMaker", map.GridCoords);
            var potato = entMan.SpawnEntity("FoodPotato", map.GridCoords);
            Assert.That(entMan.GetComponent<ProduceComponent>(potato).Seed, Is.Null, "The test needs plain produce.");
            entMan.EventBus.RaiseLocalEvent(maker, new InteractUsingEvent(farmer, potato, maker, map.GridCoords));
            Assert.That(entMan.GetComponent<WGProcessorComponent>(maker).Recipe, Is.EqualTo("WGSeedMaker"), "The seed maker refused a potato.");

            board = entMan.SpawnEntity("WGFarmOrderBoard", map.GridCoords);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(12));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGFarmPlotComponent>(tray).FertQuality, Is.GreaterThan(0), "The empty soil lost its fertilizer.");

            // Goods handed to an order by someone with no bank account stay theirs.
            var orders = entMan.GetComponent<WGOrderBoardComponent>(board).Orders;
            Assert.That(orders, Is.Not.Empty, "The board posted no orders.");
            var protos = server.ProtoMan;
            var candidates = new[] { "FoodEgg", "FoodPotato", "WheatBushel", "FoodApple", "WGFoodFriedEgg", "WGFoodQuiche" };
            var tried = false;
            foreach (var posted in orders.ToList())
            {
                var order = protos.Index<WGFarmOrderPrototype>(posted.Order);
                foreach (var id in order.Items.Select(i => i.Id).Concat(candidates))
                {
                    var goods = entMan.SpawnEntity(id, map.GridCoords);
                    var hand = new InteractUsingEvent(farmer, goods, board, map.GridCoords);
                    entMan.EventBus.RaiseLocalEvent(board, hand);
                    if (!hand.Handled)
                    {
                        entMan.DeleteEntity(goods);
                        continue;
                    }

                    tried = true;
                    Assert.That(entMan.Deleted(goods) || entMan.IsQueuedForDeletion(goods), Is.False, "The board took goods it couldn't pay for.");
                    Assert.That(posted.Filled, Is.Zero);
                    break;
                }

                if (tried)
                    break;
            }

            Assert.That(tried, Is.True, "None of the test goods fit a posted order.");
        });

        await pair.CleanReturnAsync();
    }
}
