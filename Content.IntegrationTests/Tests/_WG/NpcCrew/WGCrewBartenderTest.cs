#nullable enable
using System;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Chemistry.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

/// <summary>
/// NPC bartender: an order is poured from the dispensers' real bottles into a glass and handed to the customer, a
/// sentence is read for the drinks it names, and a drink with nothing behind the bar to pour it from is refused. His
/// HTN is put to sleep and his steps are driven by hand, so nobody walks: bartender, dispensers and customer share a
/// few adjacent tiles.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCrewBartenderSystem))]
public sealed class WGCrewBartenderTest
{
    private sealed record Scene(TestPair Pair, Entity<WGCrewBartenderComponent> Bartender, EntityUid Customer,
        EntityUid Booze, EntityUid Soda);

    private static async Task<Scene> Setup(bool dispensers = true)
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        Scene scene = default!;
        await server.WaitPost(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            foreach (var tile in new[] { new Vector2i(1, 0), new Vector2i(0, 1), new Vector2i(1, 1), new Vector2i(-1, 0) })
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, tile, map.Tile.Tile);

            var bartender = entMan.System<WGCrewSystem>().SpawnCrewman("WGCrewBartender", map.GridCoords, "bartest")!.Value;
            entMan.System<NPCSystem>().SleepNPC(bartender, entMan.GetComponent<HTNComponent>(bartender));
            var booze = EntityUid.Invalid;
            var soda = EntityUid.Invalid;
            if (dispensers)
            {
                booze = entMan.SpawnEntity("BoozeDispenser", map.GridCoords.Offset(new System.Numerics.Vector2(1, 0)));
                soda = entMan.SpawnEntity("SodaDispenser", map.GridCoords.Offset(new System.Numerics.Vector2(0, 1)));
                Stock(entMan, booze, "DrinkGinBottleFull");
                Stock(entMan, soda, "DrinkTonicWaterBottleFull");
            }

            var customer = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(1, 1)));
            scene = new Scene(pair, (bartender, entMan.GetComponent<WGCrewBartenderComponent>(bartender)), customer, booze, soda);
        });
        return scene;
    }

    /// <summary>Makes sure a dispenser holds a bottle of the kind, whether or not its pack filled it at map init.</summary>
    private static void Stock(IEntityManager entMan, EntityUid dispenser, string bottle)
    {
        var comp = entMan.GetComponent<ReagentDispenserComponent>(dispenser);
        Assert.That(comp.StorageSlots, Is.Not.Empty, "Precondition: the dispenser has storage slots.");
        var prototypes = comp.StorageSlots.Select(slot => slot.Item is { } item ? entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID : null);
        if (prototypes.Contains(bottle))
            return;

        var slots = entMan.System<ItemSlotsSystem>();
        var free = comp.StorageSlots.First(slot => slot.Item == null);
        var spawned = entMan.SpawnEntity(bottle, entMan.GetComponent<TransformComponent>(dispenser).Coordinates);
        Assert.That(slots.TryInsert(dispenser, free, spawned, null), Is.True, $"Precondition: a {bottle} goes into the dispenser.");
    }

    /// <summary>Total of a reagent in a dispenser's bottles.</summary>
    private static FixedPoint2 InBottles(IEntityManager entMan, EntityUid dispenser, string reagent)
    {
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var total = FixedPoint2.Zero;
        foreach (var slot in entMan.GetComponent<ReagentDispenserComponent>(dispenser).StorageSlots)
        {
            if (slot.Item is { } bottle && solutions.TryGetDrainableSolution(bottle, out _, out var solution))
                total += solution.GetTotalPrototypeQuantity(reagent);
        }

        return total;
    }

    /// <summary>Lets the bartender work: one step a second until <paramref name="done"/> holds or time runs out.</summary>
    private static async Task Work(Scene scene, Func<bool> done, int seconds = 30)
    {
        var server = scene.Pair.Server;
        var bartenders = server.EntMan.System<WGCrewBartenderSystem>();
        for (var i = 0; i < seconds; i++)
        {
            var finished = false;
            await server.WaitPost(() =>
            {
                finished = done();
                if (!finished)
                    bartenders.Perform(scene.Bartender);
            });
            if (finished)
                return;
            await scene.Pair.RunTicksSync(30);
        }
    }

    /// <summary>The glass of gin and tonic the customer holds, if any.</summary>
    private static EntityUid? HeldDrink(IEntityManager entMan, EntityUid customer)
    {
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        foreach (var held in entMan.System<SharedHandsSystem>().EnumerateHeld(customer))
        {
            if (solutions.TryGetDrainableSolution(held, out _, out var solution) && solution.GetTotalPrototypeQuantity("GinTonic") > 0)
                return held;
        }

        return null;
    }

    [Test]
    public async Task OrderIsPouredFromTheDispensersAndHandedOver()
    {
        var scene = await Setup();
        var server = scene.Pair.Server;
        var entMan = server.EntMan;
        var bartenders = entMan.System<WGCrewBartenderSystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var ginBefore = FixedPoint2.Zero;
        var tonicBefore = FixedPoint2.Zero;
        var glassesBefore = 0;
        await server.WaitPost(() =>
        {
            ginBefore = InBottles(entMan, scene.Booze, "Gin");
            tonicBefore = InBottles(entMan, scene.Soda, "TonicWater");
            glassesBefore = scene.Bartender.Comp.Glasses;
            Assert.That(bartenders.TryOrder(scene.Bartender, scene.Customer, "WGDrinkGinTonic"), Is.True, "The order is taken.");
            Assert.That(scene.Bartender.Comp.Queue, Has.Count.EqualTo(1));
        });

        await Work(scene, () => HeldDrink(entMan, scene.Customer) != null);
        await server.WaitAssertion(() =>
        {
            var glass = HeldDrink(entMan, scene.Customer);
            Assert.That(glass, Is.Not.Null, $"The customer holds a gin and tonic (current {scene.Bartender.Comp.Current?.Drink}, "
                + $"poured {string.Join(", ", scene.Bartender.Comp.Poured)}, glass {scene.Bartender.Comp.Glass}).");
            Assert.That(solutions.TryGetDrainableSolution(glass!.Value, out _, out var solution), Is.True);
            Assert.That(solution!.GetTotalPrototypeQuantity("GinTonic"), Is.GreaterThanOrEqualTo(FixedPoint2.New(29)),
                "The gin and the tonic reacted in the glass.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(glass.Value).EntityPrototype?.ID, Is.EqualTo("DrinkGlass"));
            Assert.That(InBottles(entMan, scene.Booze, "Gin"), Is.EqualTo(ginBefore - FixedPoint2.New(10)), "The gin came out of the bottle.");
            Assert.That(InBottles(entMan, scene.Soda, "TonicWater"), Is.EqualTo(tonicBefore - FixedPoint2.New(20)), "The tonic came out of the bottle.");
            Assert.That(scene.Bartender.Comp.Glasses, Is.EqualTo(glassesBefore - 1), "The glass came off his rack.");
            Assert.That(scene.Bartender.Comp.Current, Is.Null, "The order is done.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpeechIsReadForTheDrinksItNames()
    {
        var scene = await Setup(dispensers: false);
        var entMan = scene.Pair.Server.EntMan;
        var bartenders = entMan.System<WGCrewBartenderSystem>();
        await scene.Pair.Server.WaitAssertion(() =>
        {
            var comp = scene.Bartender.Comp;
            var both = bartenders.MatchDrinks(comp, WGCrewBartenderSystem.Normalize("Can I get a whiskey cola and a beer, please?"));
            Assert.That(both.Select(d => d.Id), Is.EqualTo(new[] { "WGDrinkWhiskeyCola", "WGDrinkBeer" }),
                "Both drinks, in order; the whiskey cola isn't also a whiskey.");
            Assert.That(bartenders.MatchDrinks(comp, WGCrewBartenderSystem.Normalize("Whisky.")).Select(d => d.Id),
                Is.EqualTo(new[] { "WGDrinkWhiskey" }));
            Assert.That(bartenders.MatchDrinks(comp, WGCrewBartenderSystem.Normalize("A G&T for me")).Select(d => d.Id),
                Is.EqualTo(new[] { "WGDrinkGinTonic" }));
            Assert.That(bartenders.MatchDrinks(comp, WGCrewBartenderSystem.Normalize("Where is the airlock?")), Is.Empty,
                "Nothing on the menu is named.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task NothingToPourFromIsRefused()
    {
        var scene = await Setup(dispensers: false);
        var entMan = scene.Pair.Server.EntMan;
        var bartenders = entMan.System<WGCrewBartenderSystem>();
        await scene.Pair.Server.WaitAssertion(() =>
        {
            Assert.That(bartenders.TryOrder(scene.Bartender, scene.Customer, "WGDrinkBeer"), Is.False,
                "No dispenser behind the bar: no beer.");
            Assert.That(scene.Bartender.Comp.Queue, Is.Empty);
        });

        await scene.Pair.CleanReturnAsync();
    }
}
