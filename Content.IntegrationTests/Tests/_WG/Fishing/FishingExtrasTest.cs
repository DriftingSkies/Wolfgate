using System.Linq;
using Content.Server._WG.Cooking;
using Content.Server._WG.Farming;
using Content.Shared._WG.Fishing;
using Content.Shared.Hands.EntitySystems;
using Content.Shared._WG.Farming;
using Content.Shared.Interaction;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Fishing;

/// <summary>Fishing's extras: the data holds together, oysters turn up pearls, and orders refuse slices.</summary>
[TestFixture]
public sealed class FishingExtrasTest
{
    /// <summary>Every extras entry names a real table, and each table has at most one extras entry.</summary>
    [Test]
    public async Task ExtrasMatchTables()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        var extras = proto.EnumeratePrototypes<WGFishExtrasPrototype>().ToList();
        Assert.That(extras, Is.Not.Empty);
        foreach (var extra in extras)
            Assert.That(proto.HasIndex(extra.Table), Is.True, $"{extra.ID} names a missing table.");
        Assert.That(extras.Select(e => e.Table).Distinct().Count(), Is.EqualTo(extras.Count), "A table has two extras entries.");

        await pair.CleanReturnAsync();
    }

    /// <summary>Shucking enough oysters on a prep board finds a pearl.</summary>
    [Test]
    public async Task OystersGivePearls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var containers = entMan.System<SharedContainerSystem>();
            var cooking = entMan.System<WGCookingSystem>();
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var board = entMan.SpawnEntity("WGPrepBoard", map.GridCoords);
            var knife = entMan.SpawnEntity("KitchenKnife", map.GridCoords);
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(cook, knife), Is.True);
            var boardEnt = (board, entMan.GetComponent<WGPrepBoardComponent>(board));
            var contents = containers.GetContainer(board, WGPrepBoardComponent.Contents);

            var pearls = 0;
            for (var i = 0; i < 100 && pearls == 0; i++)
            {
                var oyster = entMan.SpawnEntity("WGFishOyster", map.GridCoords);
                entMan.EventBus.RaiseLocalEvent(board, new InteractUsingEvent(cook, oyster, board, map.GridCoords));
                Assert.That(cooking.ChopItem(boardEnt, oyster, cook, 1f), Is.True, "An oyster didn't shuck.");
                pearls = contents.ContainedEntities.Count(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WGPearl");
                foreach (var left in contents.ContainedEntities.ToList())
                    entMan.DeleteEntity(left);
            }

            Assert.That(pearls, Is.GreaterThan(0), "A hundred oysters and not one pearl.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The fine-dishes order takes a whole pie but not a slice of one.</summary>
    [Test]
    public async Task OrdersRefuseSlices()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var proto = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var tags = entMan.System<TagSystem>();
            var order = proto.Index<WGFarmOrderPrototype>("WGOrderFineDishes");
            bool Takes(EntityUid item) => tags.HasAnyTag(item, order.Tags) && !tags.HasAnyTag(item, order.ExcludeTags);

            Assert.That(Takes(entMan.SpawnEntity("FoodPieApple", map.GridCoords)), Is.True, "The order won't take a whole pie.");
            Assert.That(Takes(entMan.SpawnEntity("FoodPieAppleSlice", map.GridCoords)), Is.False, "The order takes a slice.");
        });

        await pair.CleanReturnAsync();
    }
}
