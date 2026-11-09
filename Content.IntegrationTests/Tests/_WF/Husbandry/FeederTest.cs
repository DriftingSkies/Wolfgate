#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests._WF.Outposts;
using Content.Server._WF.Husbandry;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.OutpostRaids.RaidArrivalTest;

namespace Content.IntegrationTests.Tests._WF.Husbandry;

/// <summary>A pen's feeder feeds its hungry livestock from its storage.</summary>
[TestFixture]
[TestOf(typeof(WFHusbandrySystem))]
public sealed class FeederTest
{
    /// <summary>A starving penned chicken eats one item off the feeder and is fed to Okay; a fed one takes nothing.</summary>
    [Test]
    public async Task PennedChickenEatsFromTheFeeder()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var husbandry = server.System<WFHusbandrySystem>();
        var hungerSystem = server.System<HungerSystem>();
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var (layers, user) = await OutpostFoundingTest.Setup(pair);

        try
        {
            await server.WaitAssertion(() =>
            {
                var outpost = Found(server, layers[0], user);
                LivestockSaveTest.BuildPen(entMan, maps, tileDefs, outpost);
                var grid = entMan.GetComponent<MapGridComponent>(outpost);
                var hen = entMan.SpawnEntity("MobChicken", maps.GridTileToLocal(outpost, grid, new Vector2i(4, 1)));
                var pen = husbandry.GetPens((outpost, grid)).Single();
                Assert.That(pen.Livestock, Does.Contain(hen));

                var feeder = pen.Feeders.Single();
                var storage = entMan.GetComponent<StorageComponent>(feeder).Container;
                var containers = server.System<SharedContainerSystem>();
                for (var i = 0; i < 2; i++)
                {
                    Assert.That(containers.Insert(entMan.SpawnEntity("FoodApple", MapCoordinates.Nullspace), storage), Is.True);
                }

                var hunger = entMan.GetComponent<HungerComponent>(hen);
                hungerSystem.SetHunger(hen, 0f, hunger);
                Assert.That(husbandry.FeedPen(pen), Is.EqualTo(1), "The starving hen should eat.");
                Assert.That(hungerSystem.GetHunger(hunger), Is.GreaterThanOrEqualTo(hunger.Thresholds[HungerThreshold.Okay]));
                Assert.That(storage.ContainedEntities, Has.Count.EqualTo(1), "The hen should eat one item.");

                Assert.That(husbandry.FeedPen(pen), Is.Zero, "A fed hen shouldn't eat again.");
                Assert.That(storage.ContainedEntities, Has.Count.EqualTo(1));
            });
        }
        finally
        {
            await LeaveAndTeardown(pair, user, layers);
        }

        await pair.CleanReturnAsync();
    }
}
