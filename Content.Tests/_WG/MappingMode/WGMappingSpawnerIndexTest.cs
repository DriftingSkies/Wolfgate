#nullable enable
using System.Collections.Generic;
using Content.Shared._WG.MappingMode;
using NUnit.Framework;

namespace Content.Tests._WG.MappingMode;

[TestFixture]
public sealed class WGMappingSpawnerIndexTest
{
    private static readonly string[] None = [];

    private static WGMappingSpawnerSource Source(string id, string[] common, string[]? rare = null, float chance = 1f)
    {
        return new WGMappingSpawnerSource(id, common, rare ?? None, chance);
    }

    private static string? Pick(WGMappingSpawnerIndex index, string id)
    {
        return index.TryGetSpawner(id, out var spawner) ? spawner : null;
    }

    [Test]
    public void SpawnerOfOnlyThatPrototypeWins()
    {
        var index = WGMappingSpawnerIndex.Build(new List<WGMappingSpawnerSource>
        {
            Source("Family", ["Ian", "Lisa", "Pup"]),
            Source("OnlyIan", ["Ian"]),
        }, _ => true);

        Assert.That(Pick(index, "Ian"), Is.EqualTo("OnlyIan"));
        Assert.That(Pick(index, "Lisa"), Is.EqualTo("Family"));
        Assert.That(Pick(index, "Nobody"), Is.Null);
    }

    [Test]
    public void ChanceBelowOneIsNotExclusive()
    {
        var index = WGMappingSpawnerIndex.Build(new List<WGMappingSpawnerSource>
        {
            Source("Sometimes", ["Ian"], chance: 0.5f),
            Source("Pair", ["Ian", "Lisa"]),
        }, _ => true);

        Assert.That(Pick(index, "Ian"), Is.EqualTo("Sometimes"), "The shorter list wins among the rest.");

        index = WGMappingSpawnerIndex.Build(new List<WGMappingSpawnerSource>
        {
            Source("Sometimes", ["Ian"], chance: 0.5f),
            Source("Always", ["Ian"]),
        }, _ => true);

        Assert.That(Pick(index, "Ian"), Is.EqualTo("Always"));
    }

    [Test]
    public void CommonBeatsRareAndTiesGoToTheLowestId()
    {
        var index = WGMappingSpawnerIndex.Build(new List<WGMappingSpawnerSource>
        {
            Source("RareOnly", ["Other"], ["Ian"]),
            Source("B", ["Ian", "x"]),
            Source("A", ["Ian", "y"]),
        }, _ => true);

        Assert.That(Pick(index, "Ian"), Is.EqualTo("A"));
        Assert.That(Pick(index, "Other"), Is.EqualTo("RareOnly"));
    }

    [Test]
    public void OnlyWantedPrototypesAreIndexed()
    {
        var index = WGMappingSpawnerIndex.Build(new List<WGMappingSpawnerSource>
        {
            Source("Spawn", ["Mob", "Item"]),
        }, id => id == "Mob");

        Assert.That(index.Spawners, Has.Count.EqualTo(1));
        Assert.That(Pick(index, "Item"), Is.Null);
    }

    [Test]
    public void LoadReplacesTheTable()
    {
        var index = new WGMappingSpawnerIndex();
        index.Load(new Dictionary<string, string> { ["Mob"] = "Spawn" });
        Assert.That(Pick(index, "Mob"), Is.EqualTo("Spawn"));

        index.Load(new Dictionary<string, string>());
        Assert.That(Pick(index, "Mob"), Is.Null);
    }
}
