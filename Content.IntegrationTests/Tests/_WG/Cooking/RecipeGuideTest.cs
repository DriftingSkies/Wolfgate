using System.Collections.Generic;
using System.Linq;
using Content.Client._WG.Cooking;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>The guidebook's recipe pages list real recipes, and the homestead guides sit in their own tab.</summary>
[TestFixture]
public sealed class RecipeGuideTest
{
    [Test]
    public async Task EveryRecipeKindHasCards()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        await client.WaitIdleAsync();

        var kinds = new Dictionary<string, Dictionary<string, string>>
        {
            ["Board"] = new() { ["Kind"] = "Board" },
            ["Pan"] = new() { ["Kind"] = "Pan" },
            ["Oven"] = new() { ["Kind"] = "Oven" },
            ["Chop"] = new() { ["Kind"] = "Chop" },
            ["Stew"] = new() { ["Kind"] = "Stew" },
            ["Brew"] = new() { ["Kind"] = "Brew" },
            ["Kitchen"] = new() { ["Kind"] = "Kitchen", ["Groups"] = "Savory" },
        };

        foreach (var (kind, args) in kinds)
        {
            await client.WaitAssertion(() =>
            {
                Assert.That(new WGRecipeEmbed().TryParseTag(args, out var control), Is.True, $"The {kind} recipe list didn't build.");
                Assert.That(control!.Children.OfType<WGRecipeCard>().Count(), Is.GreaterThan(0), $"The {kind} recipe list is empty.");
            });
            await client.WaitRunTicks(1);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HomesteadIsItsOwnTab()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ProtoMan;
        var children = protos.EnumeratePrototypes<GuideEntryPrototype>().SelectMany(e => e.Children.Select(c => (Parent: e.ID, Child: c.Id))).ToList();

        Assert.That(children.Any(c => c.Child == "WGHomestead"), Is.False, "The Homestead guide isn't a top-level tab.");
        foreach (var guide in new[] { "WGCooking", "WGRecipes", "WGFarming", "WGForaging", "WGFishing", "WGHunting" })
        {
            Assert.That(children.Where(c => c.Child == guide).Select(c => c.Parent), Is.EqualTo(new[] { "WGHomestead" }),
                $"{guide} isn't under the Homestead tab alone.");
        }

        await pair.CleanReturnAsync();
    }
}
