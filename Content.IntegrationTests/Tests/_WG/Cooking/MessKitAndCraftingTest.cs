using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Cooking;
using Content.Server._WG.Farming;
using Content.Server._WG.Fishing;
using Content.Shared.Construction.NodeEntities;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>Every homestead machine can be built from the construction menu, and every job's loadout offers the mess kit.</summary>
[TestFixture]
public sealed class MessKitAndCraftingTest
{
    [Test]
    public async Task EveryMachineHasACraftingRecipe()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            // What a construction recipe builds: its graph's target node entity.
            var built = new HashSet<string>();
            foreach (var recipe in protos.EnumeratePrototypes<ConstructionPrototype>())
            {
                if (protos.TryIndex<ConstructionGraphPrototype>(recipe.Graph, out var graph)
                    && graph.Nodes.TryGetValue(recipe.TargetNode, out var node) && node.Entity is StaticNodeEntity { Id: { } entity })
                    built.Add(entity);
            }

            var machines = new[]
            {
                typeof(WGProcessorComponent), typeof(WGHearthComponent), typeof(WGPrepBoardComponent), typeof(WGSprinklerComponent),
                typeof(WGWaterCollectorComponent), typeof(WGGrowLampComponent), typeof(WGScarecrowComponent),
                typeof(WGOrderBoardComponent), typeof(WGFishingSpotComponent),
            }.Select(t => factory.GetComponentName(t)).ToList();

            var missing = protos.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract && p.ID.StartsWith("WG") && machines.Any(m => p.Components.ContainsKey(m)))
                .Select(p => p.ID)
                .Where(id => !built.Contains(id))
                .ToList();
            Assert.That(missing, Is.Empty, $"Machines with no crafting recipe: {string.Join(", ", missing)}");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryHomesteadSeedCanBeBought()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var sold = new HashSet<string>();
            foreach (var product in protos.EnumeratePrototypes<Content.Shared.Cargo.Prototypes.CargoProductPrototype>())
            {
                if (protos.TryIndex<EntityPrototype>(product.Product, out var crate)
                    && crate.TryGetComponent<Content.Shared.Storage.Components.StorageFillComponent>(out var fill, factory))
                    sold.UnionWith(fill.Contents.Select(c => c.PrototypeId?.ToString() ?? string.Empty));
            }

            var unsold = protos.EnumeratePrototypes<Content.Server.Botany.SeedPrototype>()
                .Where(s => s.ID.StartsWith("wg") && !sold.Contains(s.PacketPrototype))
                .Select(s => s.PacketPrototype)
                .ToList();
            Assert.That(unsold, Is.Empty, $"Seeds no cargo crate sells: {string.Join(", ", unsold)}");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryLoadoutOffersTheMessKitAndItsStoveCooks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var without = server.ProtoMan.EnumeratePrototypes<RoleLoadoutPrototype>()
                .Where(r => r.Groups.Count > 0 && !r.Groups.Contains("WGGroupMessKit"))
                .Select(r => r.ID)
                .ToList();
            Assert.That(without, Is.Empty, $"Loadouts without the mess kit: {string.Join(", ", without)}");

            // The kit holds its stove, and the stove burns once set on the ground, not while packed.
            var kit = entMan.SpawnEntity("WGMessKit", map.GridCoords);
            var stove = entMan.System<SharedContainerSystem>().GetContainer(kit, "storagebase").ContainedEntities
                .First(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WGCampStove");
            var cooking = entMan.System<WGCookingSystem>();
            Assert.That(cooking.Lit(stove), Is.False, "The stove burns packed in its kit.");
            entMan.System<SharedContainerSystem>().TryRemoveFromContainer(stove);
            Assert.That(cooking.Lit(stove), Is.True, "The stove set down doesn't burn.");

            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var pan = entMan.SpawnEntity("WGCookPan", map.GridCoords);
            var put = new InteractUsingEvent(cook, pan, stove, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(stove, put);
            Assert.That(put.Handled, Is.True, "The pan didn't go on the camp stove.");

            // Its folding cutting board works as a prep board.
            var board = entMan.System<SharedContainerSystem>().GetContainer(kit, "storagebase").ContainedEntities
                .First(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WGCuttingBoard");
            entMan.System<SharedContainerSystem>().TryRemoveFromContainer(board);
            var potato = entMan.SpawnEntity("FoodPotato", map.GridCoords);
            var lay = new InteractUsingEvent(cook, potato, board, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(board, lay);
            Assert.That(entMan.System<SharedContainerSystem>().GetContainer(board, WGPrepBoardComponent.Contents).ContainedEntities, Does.Contain(potato));
        });

        await pair.CleanReturnAsync();
    }
}
