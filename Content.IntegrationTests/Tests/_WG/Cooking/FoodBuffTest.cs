using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Cooking;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._WG.Cooking;
using Content.Shared._WG.Wolfmed.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>The food buffs: seasoning, organ-mending soups, spoon-feeding the laid-up, comfort, and the fighting and flying traits.</summary>
[TestFixture]
public sealed class FoodBuffTest
{
    [Test]
    public async Task SeasoningAddsATraitOrStrength()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var dish = entMan.SpawnEntity("WGFoodRoastSeeds", map.GridCoords);
            var meal = entMan.GetComponent<WGMealComponent>(dish);
            var before = meal.Strength;
            Assert.That(meal.Traits, Has.Count.EqualTo(2));

            void Use(string id)
            {
                var item = entMan.SpawnEntity(id, map.GridCoords);
                entMan.EventBus.RaiseLocalEvent(dish, new InteractUsingEvent(cook, item, dish, map.GridCoords));
            }

            // Salt has no trait, so it strengthens the dish twice its bonus.
            Use("FoodShakerSalt");
            Assert.That(meal.Strength, Is.EqualTo(before + 0.3f).Within(0.001f), "Salt didn't strengthen the dish.");

            // Chili brings Brawny, the dish's third trait.
            Use("FoodChiliPepper");
            Assert.That(meal.Traits, Does.Contain(WGMealTrait.Brawny));
            Assert.That(meal.Seasonings, Has.Count.EqualTo(2));

            // Two is the most.
            Use("FoodGarlic");
            Assert.That(meal.Seasonings, Has.Count.EqualTo(2), "A third seasoning went on.");
            Assert.That(meal.Traits, Does.Not.Contain(WGMealTrait.Fortifying));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SoupsMendOrgansUpToTheCap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var eater = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var organs = entMan.System<OrganHealthSystem>();
            var inner = entMan.System<SharedBodySystem>().GetBodyOrgans(eater)
                .Where(o => o.Component.SlotId is "heart" or "liver")
                .Select(o => (o.Id, Comp: entMan.GetComponent<WolfmedOrganComponent>(o.Id)))
                .ToList();
            Assert.That(inner, Has.Count.EqualTo(2));

            var heart = inner.First(o => entMan.GetComponent<Content.Shared.Body.Organ.OrganComponent>(o.Id).SlotId == "heart");
            var liver = inner.First(o => o.Id != heart.Id);
            organs.SetHealth((heart.Id, heart.Comp), heart.Comp.MaxHealth * 0.3f);
            organs.SetHealth((liver.Id, liver.Comp), FixedPoint2.Zero);

            for (var i = 0; i < 500; i++)
            {
                var mend = new WGMendOrgansEvent(eater, 0.08f, new List<string>(), new List<string>(), 1f, 0.8f);
                entMan.EventBus.RaiseEvent(EventSource.Local, ref mend);
            }

            Assert.That(heart.Comp.Fraction, Is.EqualTo(0.8f).Within(0.01f), "The soup didn't mend the heart up to its cap.");
            Assert.That(liver.Comp.Health, Is.EqualTo(FixedPoint2.Zero), "The soup brought back a failed liver.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ComfortFadesAway()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid warmed = default;

        // Comfort with no meal behind it fades and is removed.
        await server.WaitAssertion(() =>
        {
            warmed = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<WGMealSystem>().AddComfort(warmed, 1f);
            Assert.That(entMan.HasComponent<WGComfortComponent>(warmed), Is.True);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(20));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WGComfortComponent>(warmed), Is.False, "Faded comfort wasn't cleared.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TendingComfortAndTraits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var meals = entMan.System<WGMealSystem>();
            var nurse = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var patient = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var damage = entMan.System<DamageableSystem>();
            var hurt = new DamageSpecifier();
            hurt.DamageDict["Blunt"] = 20;
            damage.TryChangeDamage(patient, hurt, true);

            // Standing up, or with no spoon, it's just feeding.
            Assert.That(meals.Tend(nurse, patient), Is.False);
            entMan.System<StandingStateSystem>().Down(patient);
            Assert.That(meals.Tend(nurse, patient), Is.False, "Tended without a spoon.");

            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(nurse, entMan.SpawnEntity("Spoon", map.GridCoords)), Is.True);
            var before = entMan.GetComponent<DamageableComponent>(patient).TotalDamage;
            Assert.That(meals.Tend(nurse, patient), Is.True, "Spoon-feeding someone lying down didn't tend them.");
            Assert.That(entMan.GetComponent<DamageableComponent>(patient).TotalDamage, Is.LessThan(before), "Tending healed nothing.");
            Assert.That(entMan.GetComponent<WGComfortComponent>(patient).Comfort, Is.GreaterThan(0f));

            // Comfort only helps: content, then cozy, makes meals stronger.
            Assert.That(meals.ComfortFactor(nurse), Is.EqualTo(1f));
            meals.AddComfort(nurse, 50f);
            Assert.That(meals.ComfortFactor(nurse), Is.EqualTo(1.1f).Within(0.001f));
            meals.AddComfort(nurse, 40f);
            Assert.That(meals.ComfortFactor(nurse), Is.EqualTo(1.2f).Within(0.001f));

            // Steady helps at the helm, never past its cap, gentler than a stimulant.
            Assert.That(meals.HelmFactor(patient), Is.EqualTo(1f));
            meals.Give(patient, new List<WGMealTrait> { WGMealTrait.Steady, WGMealTrait.Brawny }, 5f, 10f, popup: false);
            Assert.That(meals.HelmFactor(patient), Is.GreaterThan(1f).And.LessThanOrEqualTo(1.08f));
        });

        await pair.CleanReturnAsync();
    }
}
