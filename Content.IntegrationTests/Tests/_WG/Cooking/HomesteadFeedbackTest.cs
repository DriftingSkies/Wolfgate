using System.Linq;
using Content.Server._WG.Bonfires;
using Content.Server._WG.Cooking;
using Content.Server._WG.Farming;
using Content.Server.Power.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Kitchen;
using Content.Shared.VendingMachines;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>Player feedback on the homestead: the wood hearth, dough and meat that cook alone, roomy boards, flatpacks.</summary>
[TestFixture]
public sealed class HomesteadFeedbackTest
{
    [Test]
    public async Task HearthBurnsWood()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cooking = entMan.System<WGCookingSystem>();
            var hearth = entMan.SpawnEntity("WGHearthStove", map.GridCoords);
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entMan.HasComponent<ApcPowerReceiverComponent>(hearth), Is.False, "The hearth stove still takes power.");
            Assert.That(entMan.HasComponent<AnchorableComponent>(hearth), Is.True, "The hearth stove can't be wrenched loose.");
            Assert.That(cooking.Lit(hearth), Is.False, "A new hearth burns with no wood in it.");

            var plank = entMan.SpawnEntity("MaterialWoodPlank1", map.GridCoords);
            var feed = new InteractUsingEvent(cook, plank, hearth, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(hearth, feed);
            var fire = entMan.GetComponent<WGBonfireFuelComponent>(hearth);
            Assert.That(feed.Handled && fire.Fuel > 0f, Is.True, "The hearth didn't take a plank of wood.");
            Assert.That(cooking.Lit(hearth), Is.False, "Wood lit itself.");

            entMan.System<WGBonfireFuelSystem>().SetLit((hearth, fire), true);
            Assert.That(cooking.Lit(hearth), Is.True, "A fed and lit hearth isn't burning.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DoughAndMeatCookWithoutTheBoard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var containers = entMan.System<SharedContainerSystem>();
        var map = await pair.CreateTestMap();
        EntityUid hearth = default, pan = default, roaster = default, roasterPan = default;

        string[] Held(EntityUid owner, string container) =>
            containers.GetContainer(owner, container).ContainedEntities
                .Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID ?? "?").OrderBy(id => id).ToArray();

        await server.WaitAssertion(() =>
        {
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            void Use(string item, EntityUid target)
            {
                var ev = new InteractUsingEvent(cook, entMan.SpawnEntity(item, map.GridCoords), target, map.GridCoords);
                entMan.EventBus.RaiseLocalEvent(target, ev);
                Assert.That(ev.Handled, Is.True, $"{entMan.ToPrettyString(target)} wouldn't take {item}.");
            }

            hearth = entMan.SpawnEntity("WGHearthStove", map.GridCoords);
            roaster = entMan.SpawnEntity("WGHearthStove", map.GridCoords);
            WGHearthFire.Light(entMan, hearth);
            WGHearthFire.Light(entMan, roaster);
            pan = entMan.SpawnEntity("WGCookPan", map.GridCoords);
            roasterPan = entMan.SpawnEntity("WGCookPan", map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(hearth, new InteractUsingEvent(cook, pan, hearth, map.GridCoords));
            entMan.EventBus.RaiseLocalEvent(roaster, new InteractUsingEvent(cook, roasterPan, roaster, map.GridCoords));

            // Dough goes straight in the oven; chicken, bacon and a cutlet, which upstream cooked by temperature, in the pan.
            Use("FoodDough", hearth);
            Use("FoodDoughSlice", hearth);
            Use("FoodMeatChicken", hearth);
            Use("FoodMeatBacon", hearth);
            Use("FoodMeatCutlet", hearth);
            Use("FoodCoffeeBeansRaw", roaster);
            Use("FoodSausageRaw", roaster);
            Assert.That(Held(hearth, WGHearthComponent.Oven), Is.EqualTo(new[] { "FoodDough", "FoodDoughSlice" }));
        });

        await pair.RunTicksSync(pair.SecondsToTicks(42));
        await server.WaitAssertion(() =>
        {
            Assert.That(Held(hearth, WGHearthComponent.Oven), Is.EqualTo(new[] { "FoodBreadBun", "FoodBreadPlain" }),
                "Dough left in the oven didn't bake.");
            Assert.That(Held(pan, WGPanComponent.Contents),
                Is.EqualTo(new[] { "FoodMeatBaconCooked", "FoodMeatChickenCooked", "FoodMeatCutletCooked" }), "Meat in the pan didn't cook.");

            // Coffee roasts on a shade at a time; the sausage is done.
            var roasted = Held(roasterPan, WGPanComponent.Contents);
            Assert.That(roasted, Does.Contain("FoodMeatSausage"));
            Assert.That(roasted.Any(id => id.StartsWith("FoodCoffeeBeansRoasted")), Is.True, "The coffee beans didn't roast.");
            var beans = containers.GetContainer(roasterPan, WGPanComponent.Contents).ContainedEntities
                .First(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype!.ID.StartsWith("FoodCoffeeBeansRoasted"));
            if (entMan.GetComponent<MetaDataComponent>(beans).EntityPrototype!.ID != "FoodCoffeeBeansRoastedDark")
                Assert.That(entMan.GetComponent<WGHeatingComponent>(beans).Done, Is.False, "Lightly roasted coffee stopped roasting.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Both boards hold every recipe the board makes, with a container for its reagents.</summary>
    [Test]
    public async Task BoardsHoldEveryRecipe()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            const int boardTypes = (int) (MicrowaveRecipeType.Assembler | MicrowaveRecipeType.Oven | MicrowaveRecipeType.Microwave);
            var needed = protos.EnumeratePrototypes<FoodRecipePrototype>()
                .Where(r => (r.RecipeType & boardTypes) != 0)
                .Max(r => r.IngredientsSolids.Values.Sum(v => v.Int()) + (r.IngredientsReagents.Count > 0 ? 1 : 0));
            foreach (var board in new[] { "WGPrepBoard", "WGCuttingBoard" })
            {
                Assert.That(protos.Index(board).TryGetComponent<WGPrepBoardComponent>(out var comp, factory), Is.True);
                Assert.That(comp!.Capacity, Is.GreaterThanOrEqualTo(needed), $"{board} can't hold the biggest kitchen recipe.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The folding board won't pack away loaded, so it can't carry a pile of things inside a bag.</summary>
    [Test]
    public async Task CuttingBoardPacksAwayOnlyWhenClear()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var containers = entMan.System<SharedContainerSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var kit = entMan.SpawnEntity("WGMessKit", map.GridCoords);
            var stowed = containers.GetContainer(kit, "storagebase");
            var board = stowed.ContainedEntities.First(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WGCuttingBoard");
            containers.Remove(board, stowed);

            var onion = new InteractUsingEvent(cook, entMan.SpawnEntity("FoodOnion", map.GridCoords), board, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(board, onion);
            Assert.That(containers.GetContainer(board, WGPrepBoardComponent.Contents).ContainedEntities, Has.Count.EqualTo(1));
            Assert.That(containers.Insert(board, stowed), Is.False, "A loaded cutting board packed away.");

            var onBoard = containers.GetContainer(board, WGPrepBoardComponent.Contents);
            containers.Remove(onBoard.ContainedEntities[0], onBoard);
            Assert.That(containers.Insert(board, stowed), Is.True, "A clear cutting board wouldn't pack away.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A part-loaded batch can be emptied out, handing back what went in.</summary>
    [Test]
    public async Task PartLoadedBatchEmptiesOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var packer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var table = entMan.SpawnEntity("WGFarmPackingTable", map.GridCoords);
            for (var i = 0; i < 2; i++)
                entMan.EventBus.RaiseLocalEvent(table, new InteractUsingEvent(packer, entMan.SpawnEntity("FoodCheese", map.GridCoords), table, map.GridCoords));
            var comp = entMan.GetComponent<WGProcessorComponent>(table);
            Assert.That(comp.Loading, Is.EqualTo("WGPackALuxuries"));
            Assert.That(comp.Loaded, Is.EqualTo(2));

            entMan.System<WGProcessorSystem>().Unload((table, comp), packer);
            Assert.That(comp.Loading, Is.Null, "The batch stayed loaded.");
            var cheese = 0;
            var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var meta))
            {
                if (meta.EntityPrototype?.ID == "FoodCheese" && !entMan.IsQueuedForDeletion(uid))
                    cheese++;
            }

            Assert.That(cheese, Is.EqualTo(2), "The cheese wasn't handed back.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Homestead Supply sells structures as flatpacks that a wrench unpacks into something it can also anchor.</summary>
    [Test]
    public async Task HomesteadStructuresComeFlatpacked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var wrench = entMan.SpawnEntity("Wrench", map.GridCoords);
            foreach (var id in protos.Index<VendingMachineInventoryPrototype>("WGHomesteadInventory").StartingInventory.Keys)
            {
                var proto = protos.Index(id);
                if (!proto.TryGetComponent<FlatpackComponent>(out var flatpack, factory))
                {
                    Assert.That(proto.TryGetComponent<ItemComponent>(out _, factory), Is.True, $"Homestead Supply vends {id}, a structure, unpacked.");
                    continue;
                }

                var pack = entMan.SpawnEntity(id, map.GridCoords);
                var unpack = new InteractUsingEvent(cook, wrench, pack, map.GridCoords);
                entMan.EventBus.RaiseLocalEvent(pack, unpack);
                Assert.That(unpack.Handled && (entMan.Deleted(pack) || entMan.IsQueuedForDeletion(pack)), Is.True, $"A wrench didn't unpack {id}.");

                // Found by prototype: a prep board has no fixtures for a range lookup to see.
                EntityUid made = default;
                var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
                while (query.MoveNext(out var uid, out var meta))
                {
                    if (meta.EntityPrototype?.ID == flatpack!.Entity)
                        made = uid;
                }
                Assert.That(made.Valid, Is.True, $"{id} unpacked into nothing.");
                Assert.That(entMan.HasComponent<AnchorableComponent>(made), Is.True, $"{flatpack!.Entity} can't be anchored.");
                entMan.DeleteEntity(made);
            }
        });

        await pair.CleanReturnAsync();
    }
}
