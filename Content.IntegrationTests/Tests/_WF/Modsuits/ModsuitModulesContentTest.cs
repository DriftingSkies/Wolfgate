using System.Collections.Generic;
using System.Text.RegularExpressions;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared.Guidebook;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Modsuits;

/// <summary>Checks the MODsuit module recipes, research unlocks, fabricator pack and guidebook page.</summary>
[TestFixture]
public sealed class ModsuitModulesContentTest
{
    private const string Pack = "ModsuitModules";
    private const string PlatingPack = "ModsuitPlating";
    private static readonly string[] Technologies =
    {
        "ModsuitsTech",
        "ModsuitEquipmentTech",
        "ModsuitEngineeringTech",
        "ModsuitAdvancedEngineeringTech",
        "ModsuitSupplyTech",
        "ModsuitMedicalTech",
        "ModsuitServiceTech",
        "ModsuitSecurityTech",
    };

    private static readonly EntProtoId Fabricator = "ExosuitFabricator";
    private static readonly ProtoId<GuideEntryPrototype> Guide = "WFModsuits";
    private static readonly Regex Embed = new("Entity=\"(\\w+)\"", RegexOptions.Compiled);

    [Test]
    public async Task ModulesAreFabricatedAndResearched()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var factory = pair.Server.ResolveDependency<IComponentFactory>();

            Assert.That(prototypes.TryIndex<LatheRecipePackPrototype>(Pack, out var pack), Is.True,
                $"the {Pack} recipe pack should exist");
            var recipes = pack!.Recipes;
            Assert.That(recipes, Is.Not.Empty, "the module recipe pack should not be empty");

            foreach (var recipeId in recipes)
            {
                Assert.That(prototypes.TryIndex(recipeId, out var recipe), Is.True,
                    $"recipe {recipeId} should exist");
                Assert.That(recipe!.Result, Is.Not.Null, $"recipe {recipeId} should produce an entity");
                Assert.That(recipe.Materials, Is.Not.Empty, $"recipe {recipeId} should cost materials");

                var result = recipe.Result!.Value;
                Assert.That(prototypes.TryIndex(result, out var resultProto), Is.True,
                    $"recipe {recipeId} should produce existing prototype {result}");
                Assert.That(resultProto!.TryComp<ModModuleComponent>(out _, factory) ||
                            resultProto.TryComp<ModCoreComponent>(out _, factory), Is.True,
                    $"recipe {recipeId} should produce a module or core");
            }

            var unlockedModules = new List<ProtoId<LatheRecipePrototype>>();

            foreach (var techId in Technologies)
            {
                Assert.That(prototypes.TryIndex<TechnologyPrototype>(techId, out var tech), Is.True,
                    $"technology {techId} should exist");
                Assert.That(tech!.RecipeUnlocks, Is.Not.Empty, $"technology {techId} should unlock recipes");

                foreach (var unlock in tech.RecipeUnlocks)
                {
                    Assert.That(prototypes.TryIndex(unlock, out var recipe),
                        $"technology {techId} unlocks missing recipe {unlock}");
                    if (recipe?.Result is not { } result
                        || !prototypes.TryIndex(result, out var resultProto))
                    {
                        continue;
                    }

                    if (resultProto!.TryComp<ModModuleComponent>(out _, factory)
                        || resultProto.TryComp<ModCoreComponent>(out _, factory))
                    {
                        unlockedModules.Add(unlock);
                    }
                }

                foreach (var prerequisite in tech.TechnologyPrerequisites)
                {
                    Assert.That(prototypes.HasIndex<TechnologyPrototype>(prerequisite), Is.True,
                        $"technology {techId} requires missing technology {prerequisite}");
                }
            }

            Assert.That(unlockedModules, Is.SubsetOf(recipes),
                "every researched module should be printed at the exosuit fabricator");

            Assert.That(prototypes.TryIndex<LatheRecipePackPrototype>(PlatingPack, out var platingPack), Is.True,
                $"the {PlatingPack} recipe pack should exist");
            Assert.That(platingPack!.Recipes, Is.Not.Empty, "the plating recipe pack should not be empty");

            foreach (var recipeId in platingPack.Recipes)
            {
                Assert.That(prototypes.TryIndex(recipeId, out var recipe), Is.True,
                    $"plating recipe {recipeId} should exist");
                Assert.That(recipe!.Result, Is.Not.Null, $"plating recipe {recipeId} should produce an entity");

                var result = recipe.Result!.Value;
                Assert.That(prototypes.TryIndex(result, out var resultProto), Is.True,
                    $"plating recipe {recipeId} should produce existing prototype {result}");
                Assert.That(resultProto!.ID.StartsWith("ModsuitPlating", StringComparison.Ordinal), Is.True,
                    $"plating recipe {recipeId} should produce a MODsuit plating");
            }

            foreach (var oldPackId in new[]
                     {
                         "ModsuitEngineering",
                         "ModsuitMedical",
                         "ModsuitSecurity",
                         "ModsuitSupply",
                         "ModsuitService",
                     })
            {
                Assert.That(prototypes.HasIndex<LatheRecipePackPrototype>(oldPackId), Is.False,
                    $"{oldPackId} should have moved to the exosuit fabricator");
            }

            Assert.That(prototypes.TryIndex(Fabricator, out var fabricator), Is.True);
            Assert.That(fabricator!.TryComp<LatheComponent>(out var lathe, factory), Is.True);
            Assert.That(lathe!.DynamicPacks, Does.Contain(new ProtoId<LatheRecipePackPrototype>(Pack)),
                "the exosuit fabricator should use the module recipe pack");
            Assert.That(lathe.DynamicPacks, Does.Contain(new ProtoId<LatheRecipePackPrototype>(PlatingPack)),
                "the exosuit fabricator should use the plating recipe pack");
        });

        await pair.Client.WaitAssertion(() =>
        {
            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            var resources = pair.Client.ResolveDependency<IResourceManager>();

            Assert.That(prototypes.TryIndex(Guide, out var entry), Is.True,
                "the MODsuit guidebook page should exist");

            using var file = resources.ContentFileReadText(entry!.Text);
            var text = file.ReadToEnd();
            Assert.That(text, Does.Contain("<Document>"), "the MODsuit guidebook page should be a document");

            foreach (Match match in Embed.Matches(text))
            {
                var entity = match.Groups[1].Value;
                Assert.That(prototypes.HasIndex<EntityPrototype>(entity), Is.True,
                    $"guidebook page embeds unknown entity {entity}");
            }
        });

        await pair.CleanReturnAsync();
    }
}
