using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Farming;
using Content.Shared._WG.Farming;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Farming;

/// <summary>Checks the farming data hangs together: every recipe has a machine, takes something and makes real items.</summary>
[TestFixture]
public sealed class FarmingDataTest
{
    [Test]
    public async Task EveryRecipeHasAMachineAndRealItems()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var machines = new HashSet<string>();
            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (!proto.Abstract && proto.TryGetComponent<WGProcessorComponent>(out var processor, factory))
                    machines.Add(processor.Machine);
            }

            Assert.Multiple(() =>
            {
                foreach (var recipe in protos.EnumeratePrototypes<WGProcessRecipePrototype>())
                {
                    Assert.That(machines, Does.Contain(recipe.Machine), $"{recipe.ID} names a machine nothing is.");
                    Assert.That(recipe.Auto || recipe.Inputs.Count + recipe.Tags.Count > 0 || recipe.Produce, Is.True,
                        $"{recipe.ID} takes nothing and doesn't run by itself.");
                    Assert.That(recipe.Minutes, Is.GreaterThan(0), $"{recipe.ID} takes no time.");
                    if (recipe.Needs is { } needs)
                        Assert.That(protos.HasIndex<EntityPrototype>(needs), Is.True, $"{recipe.ID} needs {needs}, which doesn't exist.");
                    Assert.That(recipe.Outputs, Is.Not.Empty, $"{recipe.ID} makes nothing.");
                    foreach (var id in recipe.Inputs.Select(i => i.Id).Concat(recipe.Outputs.Select(o => o.Id.Id)))
                        Assert.That(protos.HasIndex<EntityPrototype>(id), Is.True, $"{recipe.ID} names {id}, which doesn't exist.");
                }

                foreach (var machine in machines)
                {
                    Assert.That(protos.EnumeratePrototypes<WGProcessRecipePrototype>().Any(r => r.Machine == machine), Is.True,
                        $"The {machine} machine has no recipe.");
                }

                foreach (var order in protos.EnumeratePrototypes<WGFarmOrderPrototype>())
                {
                    Assert.That(order.Items.Count + order.Tags.Count, Is.GreaterThan(0), $"{order.ID} asks for nothing.");
                    Assert.That(order.MinCount, Is.LessThanOrEqualTo(order.MaxCount), $"{order.ID} counts are reversed.");
                    foreach (var id in order.Items)
                        Assert.That(protos.HasIndex<EntityPrototype>(id), Is.True, $"{order.ID} names {id}, which doesn't exist.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KitchenFishingAndForagingDataIsReal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            bool Real(string id) => protos.HasIndex<EntityPrototype>(id);
            Assert.Multiple(() =>
            {
                foreach (var t in protos.EnumeratePrototypes<Content.Shared._WG.Fishing.WGFishTablePrototype>())
                    foreach (var f in t.Fish)
                        Assert.That(Real(f.Id), Is.True, $"{t.ID} names {f.Id}.");
                foreach (var t in protos.EnumeratePrototypes<Content.Shared._WG.Foraging.WGForageTablePrototype>())
                    foreach (var s in t.Spots)
                        Assert.That(Real(s.Id), Is.True, $"{t.ID} names {s.Id}.");
                foreach (var r in protos.EnumeratePrototypes<Content.Shared._WG.Cooking.WGCookRecipePrototype>())
                    foreach (var id in r.Inputs.Select(i => i.Id).Append(r.Result.Id))
                        Assert.That(Real(id), Is.True, $"{r.ID} names {id}.");
                foreach (var r in protos.EnumeratePrototypes<Content.Shared._WG.Cooking.WGChopRecipePrototype>())
                    foreach (var id in r.Inputs.Select(i => i.Id).Append(r.Result.Id))
                        Assert.That(Real(id), Is.True, $"{r.ID} names {id}.");
                foreach (var r in protos.EnumeratePrototypes<Content.Shared._WG.Cooking.WGStewRecipePrototype>())
                {
                    Assert.That(protos.HasIndex<Content.Shared.Chemistry.Reagent.ReagentPrototype>(r.Soup), Is.True, $"{r.ID} makes {r.Soup}.");
                    foreach (var id in r.Inputs.Select(i => i.Id).Concat(r.Combine.SelectMany(c => c.Ids).Select(i => i.Id)))
                        Assert.That(Real(id), Is.True, $"{r.ID} names {id}.");
                }
                foreach (var r in protos.EnumeratePrototypes<Content.Shared._WG.Cooking.WGPrepRecipePrototype>())
                    foreach (var id in r.Ingredients.SelectMany(i => i.Ids).Select(i => i.Id).Append(r.Result.Id))
                        Assert.That(Real(id), Is.True, $"{r.ID} names {id}.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
