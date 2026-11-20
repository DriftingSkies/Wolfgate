using Content.Server._WG.Cooking;
using Content.Server._WG.Farming;
using Content.Shared._WG.Cooking;
using Content.Shared._WG.Farming;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Nutrition;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>Home cooking's buffs: one slot, stronger with quality, weaker as a dish goes stale; and stirring the pot.</summary>
[TestFixture]
public sealed class MealBuffTest
{
    [Test]
    public async Task MealsBuffByQualityAndFreshness()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var meals = entMan.System<WGMealSystem>();
            var eater = entMan.SpawnEntity("MobHuman", map.GridCoords);

            // A plain dish, then a gold one: the gold one is half as strong again.
            var plain = entMan.SpawnEntity("WGFoodQuiche", map.GridCoords);
            var gold = entMan.SpawnEntity("WGFoodQuiche", map.GridCoords);
            entMan.AddComponent<WGProduceQualityComponent>(gold).Quality = WGCropQuality.Gold;
            var plainStrength = meals.Strength((plain, entMan.GetComponent<WGMealComponent>(plain)));
            Assert.That(meals.Strength((gold, entMan.GetComponent<WGMealComponent>(gold))), Is.EqualTo(plainStrength * 1.5f).Within(0.01f));

            // Fresh for ten minutes, half strength once stale, never less.
            var meal = entMan.GetComponent<WGMealComponent>(plain);
            meal.Age = 300;
            Assert.That(WGMealSystem.Freshness(meal), Is.EqualTo(1f));
            meal.Age = 5000;
            Assert.That(WGMealSystem.Freshness(meal), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(WGMealSystem.Freshness(entMan.GetComponent<WGMealComponent>(entMan.SpawnEntity("WGFoodJerky", map.GridCoords))), Is.EqualTo(1f), "Jerky went stale.");

            // Finishing a dish gives its buff; a second dish replaces it.
            var ev = new AfterFullyEatenEvent(eater);
            entMan.EventBus.RaiseLocalEvent(gold, ref ev);
            var fed = entMan.GetComponent<WGWellFedComponent>(eater);
            Assert.That(fed.Traits, Is.EquivalentTo(new[] { WGMealTrait.Hearty, WGMealTrait.Fortifying, WGMealTrait.Steady }));
            var cake = entMan.SpawnEntity("WGFoodRaspberryCake", map.GridCoords);
            var ev2 = new AfterFullyEatenEvent(eater);
            entMan.EventBus.RaiseLocalEvent(cake, ref ev2);
            Assert.That(entMan.GetComponent<WGWellFedComponent>(eater).Traits, Is.EquivalentTo(new[] { WGMealTrait.Energising, WGMealTrait.Fresh }), "The second meal didn't replace the first.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StirringSpeedsTheStewAndFillsThePot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid pot = default, cook = default;

        await server.WaitAssertion(() =>
        {
            cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var hearth = entMan.SpawnEntity("WGHearthStove", map.GridCoords);
            WGHearthFire.Light(entMan, hearth);
            pot = entMan.SpawnEntity("WGCookPot", map.GridCoords);
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(pot, "pot", out var soln), Is.True);
            solutions.TryAddReagent(soln!.Value, "Water", FixedPoint2.New(100));
            var put = new Content.Shared.Interaction.InteractUsingEvent(cook, pot, hearth, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(hearth, put);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(22));
        float before = 0;
        await server.WaitAssertion(() =>
        {
            var potComp = entMan.GetComponent<WGPotComponent>(pot);
            var add = new Content.Shared.Interaction.InteractUsingEvent(cook, entMan.SpawnEntity("WGFoodPotatoSliced", map.GridCoords), pot, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(pot, add);
            before = potComp.Jobs[0].Left;
            Assert.That(entMan.System<WGCookingSystem>().Stir((pot, potComp), cook, 1f), Is.True, "The stir didn't count.");
            Assert.That(potComp.Jobs[0].Left, Is.LessThan(before - 10f), "Stirring didn't speed the stew.");
            Assert.That(entMan.System<WGCookingSystem>().Stir((pot, potComp), cook, 1f), Is.False, "A second stir straight away counted.");
        });

        await pair.RunTicksSync(pair.SecondsToTicks(55));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.System<SharedSolutionContainerSystem>().TryGetSolution(pot, "pot", out _, out var soup), Is.True);
            Assert.That(soup!.GetTotalPrototypeQuantity("WGSoupPotatoSoup"), Is.EqualTo(FixedPoint2.New(37.5f)), "A well-stirred portion wasn't bigger.");
        });

        await pair.CleanReturnAsync();
    }
}
