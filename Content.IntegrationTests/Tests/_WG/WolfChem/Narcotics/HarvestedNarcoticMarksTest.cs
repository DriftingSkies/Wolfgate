#nullable enable
using System.Linq;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Server.Botany;
using Content.Server.Botany.Systems;
using Content.Shared._Mono.ItemTax.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Contraband;
using Content.Shared.FixedPoint;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// Whatever a plant grows a street drug into carries the narcotic marks from the moment it is picked: contraband, the
/// sale tax, a place in the ship's stock and a street value the heat can read.
/// </summary>
[TestFixture]
[TestOf(typeof(WGNarcoticGoodsSystem))]
public sealed class HarvestedNarcoticMarksTest
{
    // A tomato that carries a drug gene, as the plant analyzer can splice one in.
    [TestPrototypes]
    private const string Prototypes = @"
- type: seed
  id: WGHarvestTestCrystalTomato
  name: seeds-tomato-name
  noun: seeds-noun-seeds
  displayName: seeds-tomato-display-name
  plantRsi: Objects/Specific/Hydroponics/tomato.rsi
  packetPrototype: TomatoSeeds
  productPrototypes:
  - FoodTomato
  yield: 2
  potency: 100
  chemicals:
    Desoxyephedrine:
      Min: 1
      Max: 10
      PotencyDivisor: 10
";

    private static bool Marked(IEntityManager entMan, TagSystem tags, EntityUid uid)
    {
        return entMan.HasComponent<ContrabandComponent>(uid)
            && entMan.HasComponent<ItemTaxComponent>(uid)
            && entMan.HasComponent<WGNarcoticStockComponent>(uid)
            && tags.HasTag(uid, WGNarcoticGoodsSystem.Tag);
    }

    [Test]
    public async Task SplicedDrugProduceIsMarked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var botany = server.System<BotanySystem>();
            var goods = server.System<WGNarcoticGoodsSystem>();
            var tags = server.System<TagSystem>();
            var protos = server.ResolveDependency<IPrototypeManager>();
            var picker = entMan.SpawnEntity("MobHuman", map.GridCoords);

            var tomatoes = botany.Harvest(protos.Index<SeedPrototype>("WGHarvestTestCrystalTomato"), picker).ToList();
            var plain = botany.Harvest(protos.Index<SeedPrototype>("tomato"), picker).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(tomatoes, Is.Not.Empty, "The plant gave no tomatoes.");
                Assert.That(plain, Is.Not.Empty, "The plain plant gave no tomatoes.");
                Assert.That(tomatoes.All(tomato => goods.StreetValue(tomato) > 0), Is.True, "A drug tomato has no street value for the heat to read.");
                Assert.That(tomatoes.All(tomato => Marked(entMan, tags, tomato)), Is.True, "A drug tomato isn't marked a narcotic.");
                Assert.That(plain.Any(tomato => entMan.HasComponent<ContrabandComponent>(tomato)), Is.False, "A plain tomato is contraband.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HarvestedDrugCropsAreMarked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var botany = server.System<BotanySystem>();
            var goods = server.System<WGNarcoticGoodsSystem>();
            var solutions = server.System<SharedSolutionContainerSystem>();
            var tags = server.System<TagSystem>();
            var protos = server.ResolveDependency<IPrototypeManager>();
            var picker = entMan.SpawnEntity("MobHuman", map.GridCoords);

            var pods = botany.Harvest(protos.Index<SeedPrototype>("WGOpiumPoppy"), picker).ToList();
            var leaves = botany.Harvest(protos.Index<SeedPrototype>("WGCoca"), picker).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(pods, Is.Not.Empty, "The poppy gave no pods.");
                Assert.That(leaves, Is.Not.Empty, "The coca gave no leaves.");
                Assert.That(pods.All(pod => Marked(entMan, tags, pod)), Is.True, "A harvested opium pod isn't marked a narcotic.");
                Assert.That(leaves.All(leaf => Marked(entMan, tags, leaf)), Is.True, "A harvested coca leaf isn't marked a narcotic.");
                Assert.That(pods.All(pod => goods.StreetValue(pod) > 0), Is.True, "A harvested pod has no street value for the heat to read.");
            });

            // A later change that goes through the solution system leaves the mark on.
            var pod = pods[0];
            Assert.That(solutions.TryGetSolution(pod, "food", out var soln, out _));
            solutions.TryAddReagent(soln!.Value, "Water", FixedPoint2.New(1));
            Assert.That(Marked(entMan, tags, pod), Is.True, "A pod lost its mark when its solution changed.");
        });

        await pair.CleanReturnAsync();
    }
}
