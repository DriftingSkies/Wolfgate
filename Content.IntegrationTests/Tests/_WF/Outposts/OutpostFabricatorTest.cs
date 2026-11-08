#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Outposts.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Lathe;
using Content.Server.Power.Components;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Research.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>
/// The outpost fabricator prints every recipe it has, never a console, each Wolfgate recipe worth at least the materials
/// it costs, and takes outpost recipe disks but no other blueprint.
/// </summary>
[TestFixture]
[TestOf(typeof(LatheSystem))]
public sealed class OutpostFabricatorTest
{
    private const string Fabricator = "WFOutpostFabricator";

    [TestPrototypes]
    private const string Prototypes = @"
- type: latheRecipe
  id: WFTestDiskRecipe
  result: Pen
  materials:
    Steel: 100

- type: entity
  id: WFTestOutpostDisk
  components:
  - type: Item
  - type: Tag
    tags:
    - WFOutpostRecipeDisk
  - type: Blueprint
    providedRecipes:
    - WFTestDiskRecipe

- type: entity
  id: WFTestAutolatheBlueprint
  components:
  - type: Item
  - type: Tag
    tags:
    - BlueprintAutolathe
  - type: Blueprint
    providedRecipes:
    - WFTestDiskRecipe
";

    [Test]
    public async Task EveryRecipePrintsAndPays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protos = server.ProtoMan;
        var lathes = server.System<LatheSystem>();
        var materials = server.System<SharedMaterialStorageSystem>();
        var pricing = server.System<PricingSystem>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var fabricator = entMan.SpawnEntity(Fabricator, map.GridCoords);
            var lathe = entMan.GetComponent<LatheComponent>(fabricator);
            server.System<SharedPowerReceiverSystem>().SetNeedsPower(fabricator, false);
            entMan.GetComponent<ApcPowerReceiverComponent>(fabricator).Powered = true;
            Assert.That(lathe.DynamicPacks, Is.Empty, "The fabricator researches nothing.");

            var recipes = lathes.GetAvailableRecipes(fabricator, lathe).Select(id => protos.Index(id)).ToList();
            Assert.That(recipes.Select(r => r.Result?.Id), Does.Contain("WFOutpostFabricatorPack"), "It prints another fabricator.");

            Assert.Multiple(() =>
            {
                foreach (var recipe in recipes)
                {
                    var result = protos.Index(recipe.Result!.Value);
                    Assert.That(result.TryGetComponent<WFOutpostKitComponent>(out _, factory) || result.TryGetComponent<WFOutpostConsoleComponent>(out _, factory), Is.False,
                        $"{recipe.ID} prints an outpost console.");

                    // Stock recipes keep upstream's prices; Wolfgate's own are worth what they cost.
                    var cost = recipe.Materials.Sum(m => protos.Index(m.Key).Price * m.Value);
                    if (recipe.ID.StartsWith("WF"))
                    {
                        Assert.That(pricing.GetEstimatedPrice(result), Is.GreaterThanOrEqualTo(cost),
                            $"{result.ID} is worth less than the {cost:F1} its materials cost at the fabricator.");
                    }

                    Fill(recipe);
                    Assert.That(lathes.CanProduce(fabricator, recipe, 1, lathe), Is.True, $"The fabricator can't print {recipe.ID}.");
                }
            });

            void Fill(LatheRecipePrototype recipe)
            {
                foreach (var (material, amount) in recipe.Materials)
                {
                    var stock = materials.GetMaterialAmount(fabricator, material);
                    if (stock < amount)
                        Assert.That(materials.TryChangeMaterialAmount(fabricator, material, amount - stock), $"The fabricator won't take {material}.");
                }
            }

            // Its own recipes print for real, one of each.
            foreach (var recipe in recipes.Where(r => OwnPack(protos, r)))
            {
                var before = Count(entMan, recipe.Result!.Value);
                Fill(recipe);
                Assert.That(lathes.TryAddToQueue(fabricator, recipe, 1, lathe), Is.True, $"{recipe.ID} wasn't queued.");
                Assert.That(lathes.TryStartProducing(fabricator, lathe), Is.True, $"{recipe.ID} didn't start.");
                lathes.FinishProducing(fabricator, lathe);
                Assert.That(Count(entMan, recipe.Result!.Value), Is.EqualTo(before + 1), $"{recipe.ID} didn't print.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TakesOnlyOutpostDisks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var lathes = server.System<LatheSystem>();
        var blueprints = server.System<BlueprintSystem>();

        await server.WaitAssertion(() =>
        {
            var fabricator = entMan.SpawnEntity(Fabricator, map.GridCoords);
            var lathe = entMan.GetComponent<LatheComponent>(fabricator);
            var receiver = (fabricator, entMan.GetComponent<BlueprintReceiverComponent>(fabricator));
            var recipe = new ProtoId<LatheRecipePrototype>("WFTestDiskRecipe");
            Assert.That(lathes.GetAvailableRecipes(fabricator, lathe), Does.Not.Contain(recipe));

            var other = entMan.SpawnEntity("WFTestAutolatheBlueprint", map.GridCoords);
            Assert.That(blueprints.TryInsertBlueprint(receiver, (other, entMan.GetComponent<BlueprintComponent>(other)), null), Is.False,
                "An autolathe blueprint went into the outpost fabricator.");

            var disk = entMan.SpawnEntity("WFTestOutpostDisk", map.GridCoords);
            Assert.That(blueprints.TryInsertBlueprint(receiver, (disk, entMan.GetComponent<BlueprintComponent>(disk)), null), Is.True,
                "The outpost disk was refused.");
            Assert.That(lathes.GetAvailableRecipes(fabricator, lathe), Does.Contain(recipe), "The disk's recipe is printable.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Each tier of workshop disk goes into the fabricator and makes every board on it printable.</summary>
    [Test]
    public async Task WorkshopDisksTeachTheirBoards([Values("WFOutpostRecipeDiskWorkshop", "WFOutpostRecipeDiskIndustrial", "WFOutpostRecipeDiskAdvanced")] string diskId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var lathes = server.System<LatheSystem>();
        var blueprints = server.System<BlueprintSystem>();

        await server.WaitAssertion(() =>
        {
            var fabricator = entMan.SpawnEntity(Fabricator, map.GridCoords);
            var lathe = entMan.GetComponent<LatheComponent>(fabricator);
            var disk = entMan.SpawnEntity(diskId, map.GridCoords);
            var blueprint = entMan.GetComponent<BlueprintComponent>(disk);
            var taught = blueprint.ProvidedRecipes.ToList();
            Assert.That(taught, Is.Not.Empty);
            foreach (var recipe in taught)
            {
                Assert.That(server.ProtoMan.HasIndex(recipe), $"{diskId} names a missing recipe {recipe}.");
                Assert.That(lathes.GetAvailableRecipes(fabricator, lathe), Does.Not.Contain(recipe), $"{recipe} is printable without the disk.");
            }

            Assert.That(blueprints.TryInsertBlueprint((fabricator, entMan.GetComponent<BlueprintReceiverComponent>(fabricator)), (disk, blueprint), null),
                Is.True, $"{diskId} was refused.");
            var available = lathes.GetAvailableRecipes(fabricator, lathe);
            foreach (var recipe in taught)
            {
                Assert.That(available, Does.Contain(recipe), $"{recipe} isn't printable with {diskId} in.");
            }
        });

        await pair.CleanReturnAsync();
    }

    private static bool OwnPack(IPrototypeManager protos, LatheRecipePrototype recipe)
    {
        return protos.EnumeratePrototypes<LatheRecipePackPrototype>()
            .Any(p => p.ID.StartsWith("WFOutpost") && p.Recipes.Contains(recipe.ID));
    }

    private static int Count(IEntityManager entMan, EntProtoId proto)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var meta))
        {
            if (meta.EntityPrototype?.ID == proto.Id)
                count++;
        }

        return count;
    }
}
