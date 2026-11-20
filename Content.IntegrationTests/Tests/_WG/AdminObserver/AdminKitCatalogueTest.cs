#nullable enable
using System.Linq;
using Content.Server._WG.AdminObserver;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Item;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.AdminObserver;

/// <summary>The kit catalogue names real items, and holds everything the default kit hands out.</summary>
[TestFixture]
[TestOf(typeof(WGAdminKitCategoryPrototype))]
public sealed class AdminKitCatalogueTest
{
    [Test]
    public async Task EveryEntrySpawnsAndTheDefaultKitIsListed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var catalogue = server.System<WGAdminKitCatalogueSystem>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var categories = catalogue.Categories();
            Assert.That(categories, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                foreach (var category in categories)
                {
                    Assert.That(loc.HasString(category.Name), $"{category.ID} has no name string.");
                    Assert.That(category.Entries, Is.Not.Empty, $"{category.ID} is empty.");

                    foreach (var entry in category.Entries)
                    {
                        if (!protoMan.TryIndex(entry, out var prototype) || prototype.Abstract)
                        {
                            Assert.Fail($"{category.ID} lists {entry}, which is not a spawnable entity.");
                            continue;
                        }

                        var item = entMan.SpawnEntity(entry, map.GridCoords);
                        Assert.That(entMan.HasComponent<ItemComponent>(item), $"{entry} in {category.ID} is not an item.");
                        entMan.DeleteEntity(item);
                    }
                }

                var all = categories.SelectMany(c => c.Entries).Select(e => e.Id).ToList();
                Assert.That(all, Is.Unique, "An item is listed in two categories.");

                var gear = protoMan.Index(WGAdminObserverKitSystem.DefaultGear);
                foreach (var id in gear.Equipment.Values.Concat(gear.Storage.Values.SelectMany(s => s)).Select(p => p.Id))
                {
                    Assert.That(catalogue.Contains(id), $"{id} is in the default kit but not in the catalogue.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
