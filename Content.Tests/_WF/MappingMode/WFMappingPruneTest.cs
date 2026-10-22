using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.MappingMode;
using NUnit.Framework;

namespace Content.Tests._WF.MappingMode;

[TestFixture]
public sealed class WFMappingPruneTest
{
    private static List<WFMappingVersionName> Versions(string kinds)
    {
        // One character per version, oldest first: m for manual, a for auto, u for upload.
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return kinds.Select((k, i) => new WFMappingVersionName(i + 1, start.AddMinutes(i), k switch
        {
            'a' => WFMappingVersionKind.Auto,
            'u' => WFMappingVersionKind.Upload,
            _ => WFMappingVersionKind.Manual,
        })).ToList();
    }

    private static int[] Pruned(string kinds, int max)
    {
        return WFMappingPrune.SelectForRemoval(Versions(kinds), max).Select(v => v.Number).ToArray();
    }

    [Test]
    public void UnderOrAtTheLimitKeepsEverything()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pruned("mam", 5), Is.Empty);
            Assert.That(Pruned("mam", 3), Is.Empty);
            Assert.That(Pruned("", 1), Is.Empty);
        });
    }

    [Test]
    public void AutosavesGoBeforeManualSaves()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pruned("mamam", 3), Is.EqualTo(new[] { 2, 4 }));
            Assert.That(Pruned("mamam", 2), Is.EqualTo(new[] { 2, 4, 1 }));
            Assert.That(Pruned("mmmma", 2), Is.EqualTo(new[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void UploadsArePrunedLikeManualSaves()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pruned("umam", 3), Is.EqualTo(new[] { 3 }), "The autosave goes before the upload.");
            Assert.That(Pruned("umam", 2), Is.EqualTo(new[] { 3, 1 }), "Then the oldest of uploads and manual saves.");
            Assert.That(Pruned("muau", 2), Is.EqualTo(new[] { 3, 1 }));
            Assert.That(Pruned("uu", 1), Is.EqualTo(new[] { 1 }), "The newest upload stays.");
        });
    }

    [Test]
    public void OldestGoFirst()
    {
        Assert.That(Pruned("aaaa", 2), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void NewestAlwaysStays()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pruned("ma", 1), Is.EqualTo(new[] { 1 }));
            Assert.That(Pruned("aa", 1), Is.EqualTo(new[] { 1 }));
            Assert.That(Pruned("mma", 0), Is.EqualTo(new[] { 1, 2 }), "A limit below one keeps one.");
        });
    }
}
