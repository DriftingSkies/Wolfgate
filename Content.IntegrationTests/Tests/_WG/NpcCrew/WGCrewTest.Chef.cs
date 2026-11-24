#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.Kitchen.Components;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Kitchen.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    private const string ChefDish = "FoodMealPotatoLoaded";

    // A microwave that counts as powered without an APC.
    [TestPrototypes]
    private const string ChefPrototypes = @"
- type: entity
  parent: KitchenMicrowave
  id: WGTestChefMicrowave
  components:
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <summary>
    /// With his one dish missing from the counter, the chef takes its ingredients from the pantry, cooks them in the
    /// real microwave and puts the dish on the table beside his post; with the stores spent he makes no more.
    /// </summary>
    [Test]
    public async Task ChefCooksMissingDishFromStores()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 9, gravity: true);
        var chefs = Server.System<WGCrewChefSystem>();
        EntityUid chef = default, pantry = default, oven = default, counter = default;
        await Server.WaitAssertion(() =>
        {
            EntityCoordinates At(float x, float y) => new(deck, new Vector2(x, y));
            var storage = Server.System<EntityStorageSystem>();

            // The counter beside the post, and a prep table across from it with the microwave on it.
            counter = SEntMan.SpawnAtPosition("TableReinforced", At(4.5f, 3.5f));
            SEntMan.SpawnAtPosition("TableReinforced", At(6.5f, 4.5f));
            oven = SEntMan.SpawnAtPosition("WGTestChefMicrowave", At(6.5f, 4.5f));
            pantry = SEntMan.SpawnAtPosition("WGCrewChefPantry", At(2.5f, 4.5f));
            Assert.That(storage.Insert(SEntMan.SpawnAtPosition("FoodPotato", At(2.5f, 4.5f)), pantry), Is.True);
            Assert.That(storage.Insert(SEntMan.SpawnAtPosition("FoodCheeseSlice", At(2.5f, 4.5f)), pantry), Is.True);

            chef = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Chef, At(4.5f, 4.5f), "galley")!.Value;
            var comp = SEntMan.GetComponent<WGCrewChefComponent>(chef);
            comp.Menu = new() { new WGCrewChefDish { Recipe = "RecipeLoadedBakedPotato", Stock = 1 } };
            // Nothing comes aboard during the test: what he cooks is what the pantry held.
            comp.RestockInterval = TimeSpan.FromHours(1);
            Assert.That(SEntMan.GetComponent<WGCrewComponent>(chef).Duty, Is.EqualTo(WGCrewDuties.Chef));
        });

        await WaitUntil(() => DishesOn(counter) == 1, 3000,
            () => $"The chef should cook the missing dish and put it on the counter. job={chefs.HasJob(chef)} " +
                  $"oven={SEntMan.GetComponent<MicrowaveComponent>(oven).Storage.ContainedEntities.Count} {Describe(chef)}");

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<EntityStorageComponent>(pantry).Contents.ContainedEntities, Is.Empty,
                "The ingredients come out of the pantry.");
            Assert.That(SEntMan.HasComponent<ActiveMicrowaveComponent>(oven), Is.False);
            Assert.That(SEntMan.GetComponent<MicrowaveComponent>(oven).Storage.ContainedEntities, Is.Empty);
            Assert.That(Server.System<SharedHandsSystem>().EnumerateHeld(chef).Any(), Is.False, "He puts the dish down.");
        });

        // The counter is stocked: he leaves it be.
        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(DishesOnDeck(deck), Is.EqualTo(1));
            Assert.That(chefs.HasJob(chef), Is.False);
            // A diner takes it.
            foreach (var dish in DishesOnDeckList(deck))
            {
                SEntMan.DeleteEntity(dish);
            }
        });

        // With the stores spent there is nothing to cook it from, and nothing appears from nowhere.
        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(DishesOnDeck(deck), Is.Zero);
            Assert.That(chefs.HasJob(chef), Is.False);
        });
    }

    /// <summary>A pantry is restocked a dish's worth at a time, only up to its cap, and not at all without a pantry.</summary>
    [Test]
    public async Task ChefPantryRestockIsBounded()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var chefs = Server.System<WGCrewChefSystem>();
            var post = new EntityCoordinates(deck, new Vector2(2.5f));
            var chef = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Chef, post, "pantry")!.Value;
            var comp = SEntMan.GetComponent<WGCrewChefComponent>(chef);
            Assert.That(chefs.Restock((chef, comp), deck), Is.False, "No pantry aboard, no provisions.");

            var pantry = SEntMan.SpawnAtPosition("WGCrewChefPantry", new EntityCoordinates(deck, new Vector2(0.5f, 0.5f)));
            var limit = comp.Menu.Count * comp.PantrySets;
            var added = 0;
            while (added <= limit && chefs.Restock((chef, comp), deck))
            {
                added++;
            }

            Assert.That(added, Is.InRange(1, limit), "Each restock adds one dish's worth, and it stops at the cap.");
            Assert.That(chefs.Restock((chef, comp), deck), Is.False);

            var contents = SEntMan.GetComponent<EntityStorageComponent>(pantry).Contents.ContainedEntities;
            int Count(string id) => contents.Count(item => SEntMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == id);
            Assert.That(Count("FoodMeat"), Is.EqualTo(comp.PantrySets));
            Assert.That(Count("FoodSausageRaw"), Is.EqualTo(comp.PantrySets));
            Assert.That(Count("FoodPotato"), Is.EqualTo(comp.PantrySets));
            Assert.That(Count("FoodBreadBun"), Is.EqualTo(2 * comp.PantrySets), "The burger and the hotdog each need a bun.");
        });
    }

    /// <summary>The chef's dishes lying uncontained on a table.</summary>
    private int DishesOn(EntityUid table)
    {
        var at = Server.System<SharedTransformSystem>().GetMapCoordinates(table);
        return Server.System<EntityLookupSystem>().GetEntitiesInRange(at, 0.75f, LookupFlags.Uncontained)
            .Count(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == ChefDish);
    }

    /// <summary>The chef's dishes anywhere on the deck, held or not.</summary>
    private int DishesOnDeck(EntityUid deck) => DishesOnDeckList(deck).Count;

    private List<EntityUid> DishesOnDeckList(EntityUid deck)
    {
        var found = new List<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID == ChefDish && xform.GridUid == deck)
                found.Add(uid);
        }

        return found;
    }
}
