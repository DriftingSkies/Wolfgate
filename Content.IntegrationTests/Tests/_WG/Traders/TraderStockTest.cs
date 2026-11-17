#nullable enable
using System.Collections.Generic;
using Content.Shared.Item;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Traders;

[TestFixture]
public sealed class TraderStockTest
{
    /// <summary>
    /// Every line of a travelling trader's stock is something that can be handed over: a real item, not a marker or
    /// a fixture, and with stock to roll.
    /// </summary>
    [Test]
    public async Task TravellingStockIsRealAndPriced()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var failures = new List<string>();
        await server.WaitPost(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var factory = server.ResolveDependency<IComponentFactory>();
            foreach (var inventory in prototypes.EnumeratePrototypes<VendingMachineInventoryPrototype>())
            {
                if (!inventory.ID.StartsWith("WGTrader"))
                    continue;

                foreach (var (id, count) in inventory.StartingInventory)
                {
                    if (!prototypes.TryIndex<EntityPrototype>(id, out var item))
                        failures.Add($"{inventory.ID}: {id} does not exist");
                    else if (item.Abstract || !item.TryComp<ItemComponent>(out _, factory))
                        failures.Add($"{inventory.ID}: {id} is not an item");
                    else if (count == 0)
                        failures.Add($"{inventory.ID}: {id} has no stock");
                }
            }
        });

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        await pair.CleanReturnAsync();
    }
}
