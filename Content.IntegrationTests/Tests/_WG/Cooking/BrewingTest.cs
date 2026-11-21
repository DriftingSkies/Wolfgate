using System.Collections.Generic;
using Content.Server._WG.Cooking;
using Content.Shared._WG.Cooking;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>Brewing in a kettle, and the cup buff that sits beside a meal's.</summary>
[TestFixture]
public sealed class BrewingTest
{
    [Test]
    public async Task OnlyAKettleBrews()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var power = entMan.System<SharedPowerReceiverSystem>();
        var map = await pair.CreateTestMap();

        EntityUid cook = default, kettleHearth = default, potHearth = default, kettle = default, pot = default;

        bool Use(EntityUid used, EntityUid target)
        {
            var ev = new InteractUsingEvent(cook, used, target, entMan.GetComponent<TransformComponent>(target).Coordinates);
            entMan.EventBus.RaiseLocalEvent(target, ev);
            return ev.Handled;
        }

        FixedPoint2 Holds(EntityUid owner, string solution, string reagent)
        {
            Assert.That(solutions.TryGetSolution(owner, solution, out _, out var soln), Is.True);
            return soln!.GetTotalPrototypeQuantity(reagent);
        }

        await server.WaitAssertion(() =>
        {
            cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            kettleHearth = entMan.SpawnEntity("KitchenElectricRange", map.GridCoords);
            potHearth = entMan.SpawnEntity("KitchenElectricRange", map.GridCoords);
            power.SetNeedsPower(kettleHearth, false);
            power.SetNeedsPower(potHearth, false);

            kettle = entMan.SpawnEntity("WGCookKettle", map.GridCoords);
            pot = entMan.SpawnEntity("WGCookPot", map.GridCoords);
            Assert.That(entMan.GetComponent<WGPotComponent>(kettle).Kettle, Is.True);
            Assert.That(entMan.GetComponent<WGPotComponent>(pot).Kettle, Is.False);
            foreach (var vessel in new[] { kettle, pot })
            {
                Assert.That(solutions.TryGetSolution(vessel, "pot", out var soln), Is.True);
                solutions.TryAddReagent(soln!.Value, "Water", FixedPoint2.New(100));
            }

            Assert.That(Use(kettle, kettleHearth), Is.True);
            Assert.That(Use(pot, potHearth), Is.True);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(22));
        await server.WaitAssertion(() =>
        {
            // Fresh tea leaves steep in the kettle; the pot won't take them.
            Assert.That(Use(entMan.SpawnEntity("LeavesTea", map.GridCoords), kettle), Is.True);
            Assert.That(entMan.GetComponent<WGPotComponent>(kettle).Jobs, Has.Count.EqualTo(1));
            Use(entMan.SpawnEntity("LeavesTea", map.GridCoords), pot);
            Assert.That(entMan.GetComponent<WGPotComponent>(pot).Jobs, Is.Empty, "A pot took tea leaves.");

            // Tea powder steeps in the kettle, and sits idle in the pot.
            Assert.That(solutions.TryGetSolution(kettle, "pot", out var kettleSoln), Is.True);
            solutions.TryAddReagent(kettleSoln!.Value, "TeaPowder", FixedPoint2.New(5));
            Assert.That(solutions.TryGetSolution(pot, "pot", out var potSoln), Is.True);
            solutions.TryAddReagent(potSoln!.Value, "TeaPowder", FixedPoint2.New(5));
        });

        await pair.RunTicksSync(pair.SecondsToTicks(18));
        await server.WaitAssertion(() =>
        {
            Assert.That(Holds(kettle, "pot", "WGTeaGreen"), Is.EqualTo(FixedPoint2.New(30)), "The leaves didn't brew green tea.");
            Assert.That(Holds(kettle, "pot", "WGTeaBlack"), Is.EqualTo(FixedPoint2.New(25)), "The powder didn't brew black tea.");
            Assert.That(Holds(kettle, "pot", "TeaPowder"), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(Holds(pot, "pot", "TeaPowder"), Is.EqualTo(FixedPoint2.New(5)), "The pot brewed tea.");

            // Powder and hot water no longer react into tea or coffee anywhere else.
            var protos = server.ResolveDependency<IPrototypeManager>();
            foreach (var reaction in new[] { "Tea", "GreenTea", "ChamomileTea", "BerryTea", "FruitTea", "Coffee", "DecafCoffee" })
                Assert.That(protos.HasIndex<ReactionPrototype>(reaction), Is.False, $"The {reaction} reaction still brews outside a kettle.");

            // A cup used on the hearth pours tea out of the kettle, before the water.
            var cup = entMan.SpawnEntity("WGCookCup", map.GridCoords);
            Assert.That(Use(cup, kettleHearth), Is.True);
            Assert.That(Holds(cup, "drink", "Water"), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(Holds(cup, "drink", "WGTeaGreen") + Holds(cup, "drink", "WGTeaBlack"), Is.EqualTo(FixedPoint2.New(30)));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CupSitsBesideTheMeal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var meals = entMan.System<WGMealSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var drinker = entMan.SpawnEntity("MobHuman", map.GridCoords);
            meals.Give(drinker, new List<WGMealTrait> { WGMealTrait.Hearty }, 1f, 10f, popup: false);

            var coffee = new WGMealBuffEvent(drinker, new List<WGMealTrait> { WGMealTrait.Energising, WGMealTrait.WideAwake }, 0.8f, 6f, Cup: true);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref coffee);
            Assert.That(entMan.GetComponent<WGWellFedComponent>(drinker).Traits, Is.EqualTo(new[] { WGMealTrait.Hearty }), "The cup replaced the meal.");
            Assert.That(entMan.GetComponent<WGWarmCupComponent>(drinker).Traits, Does.Contain(WGMealTrait.WideAwake));
            Assert.That(meals.TraitStrength(drinker, WGMealTrait.Energising), Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(entMan.GetComponent<WGComfortComponent>(drinker).Comfort, Is.GreaterThan(0f), "A warm sip gave no comfort.");

            // Another drink takes the cup slot; the meal stays.
            var tea = new WGMealBuffEvent(drinker, new List<WGMealTrait> { WGMealTrait.Numbed, WGMealTrait.Steeled }, 0.8f, 6f, Cup: true);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref tea);
            Assert.That(entMan.GetComponent<WGWarmCupComponent>(drinker).Traits, Is.EqualTo(new[] { WGMealTrait.Numbed, WGMealTrait.Steeled }));
            Assert.That(meals.TraitStrength(drinker, WGMealTrait.Energising), Is.EqualTo(0f));
            Assert.That(entMan.HasComponent<WGWellFedComponent>(drinker), Is.True);

            // Numbed eases an injury's slowdown; Steeled takes the edge off a blow's stamina.
            var slow = new ModifySlowOnDamageSpeedEvent(0.5f);
            entMan.EventBus.RaiseLocalEvent(drinker, ref slow);
            Assert.That(slow.Speed, Is.GreaterThan(0.5f).And.LessThan(1f));
            var blow = new BeforeStaminaDamageEvent(20f);
            entMan.EventBus.RaiseLocalEvent(drinker, ref blow);
            Assert.That(blow.Value, Is.LessThan(20f).And.GreaterThan(15f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SharingAPotIsLikeSharingAMeal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var first = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var second = entMan.SpawnEntity("MobHuman", map.GridCoords);

            void Sip(EntityUid who, WGMealTrait trait)
            {
                var ev = new WGMealBuffEvent(who, new List<WGMealTrait> { trait }, 0.8f, 6f, Cup: true, Comfort: 0.4f);
                entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            }

            float Comfort(EntityUid who) => entMan.GetComponent<WGComfortComponent>(who).Comfort;

            Sip(first, WGMealTrait.Steady);
            Assert.That(Comfort(first), Is.EqualTo(0.4f).Within(0.01f), "Drinking alone was shared.");

            Sip(second, WGMealTrait.Steady);
            Assert.That(Comfort(first), Is.GreaterThan(10f), "Sharing a pot gave the first drinker nothing.");
            Assert.That(Comfort(second), Is.GreaterThan(10f), "Sharing a pot gave the second drinker nothing.");

            // Shared once in a while, not on every sip.
            var before = Comfort(first);
            Sip(first, WGMealTrait.Steady);
            Assert.That(Comfort(first), Is.EqualTo(before + 0.4f).Within(0.01f));

            // Chamomile soothes from the first sip: comfort builds half as fast again.
            var calm = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(20f, 0f)));
            Sip(calm, WGMealTrait.Soothed);
            Sip(calm, WGMealTrait.Soothed);
            Assert.That(Comfort(calm), Is.EqualTo(0.6f + 0.6f).Within(0.01f));
        });

        await pair.CleanReturnAsync();
    }
}
