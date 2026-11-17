#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WG.Spawning;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Streets;

/// <summary>Every ported street entity sits in the spawn menu's Streets group, each folder in its own sub-folder.</summary>
[TestFixture]
public sealed class StreetSpawnMenuTest
{
    [Test]
    public async Task StreetEntitiesHaveTheirOwnFolders()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        await client.WaitAssertion(() =>
        {
            var classifier = new WolfgateSpawnClassifier(client.ProtoMan, client.EntMan.ComponentFactory);
            var streets = client.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract && p.Categories.Any(c => c.ID == "WGStreets") && p.Categories.All(c => c.ID != "WGVehicles")).ToList();
            Assert.That(streets, Has.Count.GreaterThan(500));

            var sorted = streets.GroupBy(p => classifier.Classify(p)).ToDictionary(g => g.Key, g => g.Count());
            foreach (var (category, count) in sorted)
                Assert.That(WgSpawnCategories.GroupFor(category), Is.EqualTo(WgSpawnGroup.Streets), $"{count} street entities went to {category}.");
            foreach (var folder in new[] { WgSpawnCategory.StreetFurniture, WgSpawnCategory.StreetSigns, WgSpawnCategory.StreetLighting,
                         WgSpawnCategory.StreetBuildings, WgSpawnCategory.StreetYards })
                Assert.That(sorted.GetValueOrDefault(folder), Is.GreaterThan(0), $"{folder} is empty.");
        });
        await pair.CleanReturnAsync();
    }
}
