using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.AdminObserver;
using Content.Shared._WG.AdminObserver;
using NUnit.Framework;

namespace Content.Tests._WG.AdminObserver;

/// <summary>Checks an admin's observer document survives storage and is cleaned up on the way in.</summary>
[TestFixture]
[TestOf(typeof(WGAdminObserverSerializer))]
public sealed class WGAdminObserverDataTest
{
    private static readonly HashSet<string> Entities = new() { "Bag", "Pouch", "Wrench", "Crate", "Corgi" };
    private static readonly HashSet<string> Gear = new() { "Suit" };

    private static WGAdminObserverData Clean(WGAdminObserverData data)
    {
        return WGAdminObserverSerializer.Sanitize(data, Entities.Contains, Gear.Contains);
    }

    private static WGKitNode Node(string prototype, params WGKitStored[] contents)
    {
        return new WGKitNode { Prototype = prototype, Contents = contents.ToList() };
    }

    private static WGKitStored At(int x, int y, int rotation, WGKitNode node)
    {
        return new WGKitStored { Node = node, X = x, Y = y, Rotation = rotation };
    }

    [Test]
    public void RoundTripKeepsEverything()
    {
        var data = new WGAdminObserverData
        {
            DefaultPreset = "Dog",
            Presets = new()
            {
                new WGAvatarPreset { Name = "Dog", Kind = WGAvatarKind.Mob, Prototype = "Corgi", DisplayName = "Ian" },
                new WGAvatarPreset
                {
                    Name = "Me",
                    Kind = WGAvatarKind.Character,
                    CharacterSlot = 2,
                    Outfit = "Suit",
                    EntryStyle = WGEntryStyle.Silent,
                    Description = "Looks official.",
                },
            },
            Marker = new WGStaffMarkerSettings
            {
                Wording = WGMarkerWording.RankAndName,
                Visuals = WGMarkerVisuals.Nameplate | WGMarkerVisuals.Ring,
                Ring = "placement",
                Color = "#fe019a",
            },
            Kit = new WGKitLayout
            {
                Slots = new() { ["back"] = Node("Bag", At(1, 2, 90, Node("Pouch", At(0, 0, 0, Node("Wrench"))))) },
                Hands = new() { ["left"] = Node("Wrench") },
            },
        };

        Assert.That(WGAdminObserverSerializer.TrySerialize(Clean(data), out var json));
        var read = WGAdminObserverSerializer.Deserialize(json);
        Assert.That(read, Is.Not.Null);
        Clean(read!);

        Assert.That(WGAdminObserverSerializer.TrySerialize(read!, out var again));
        Assert.That(again, Is.EqualTo(json), "A stored document should read back the same.");

        Assert.Multiple(() =>
        {
            Assert.That(read!.Version, Is.EqualTo(WGAdminObserverData.CurrentVersion));
            Assert.That(read.DefaultPreset, Is.EqualTo("Dog"));
            Assert.That(read.Presets.Select(p => p.Name), Is.EqualTo(new[] { "Dog", "Me" }));
            Assert.That(read.Presets[1].CharacterSlot, Is.EqualTo(2));
            Assert.That(read.Presets[1].EntryStyle, Is.EqualTo(WGEntryStyle.Silent));
            Assert.That(read.Marker.Visuals, Is.EqualTo(WGMarkerVisuals.Nameplate | WGMarkerVisuals.Ring));
            Assert.That(read.Marker.Color, Is.EqualTo("#fe019a"));

            var stored = read.Kit!.Slots["back"].Contents.Single();
            Assert.That((stored.X, stored.Y, stored.Rotation), Is.EqualTo((1, 2, 90)));
            Assert.That(stored.Node.Contents.Single().Node.Prototype, Is.EqualTo("Wrench"));
            Assert.That(read.Kit.Hands["left"].Prototype, Is.EqualTo("Wrench"));
        });
    }

    [Test]
    public void VersionIsWrittenAndNewerOnesRefused()
    {
        Assert.That(WGAdminObserverSerializer.TrySerialize(new WGAdminObserverData(), out var json));
        Assert.That(json, Does.Contain($"\"Version\":{WGAdminObserverData.CurrentVersion}"));

        var newer = json.Replace($"\"Version\":{WGAdminObserverData.CurrentVersion}", "\"Version\":999");
        Assert.Multiple(() =>
        {
            Assert.That(WGAdminObserverSerializer.Deserialize(newer), Is.Null, "A newer document should not be read.");
            Assert.That(WGAdminObserverSerializer.Deserialize("{ not json"), Is.Null);
        });
    }

    [Test]
    public void UnknownPrototypesAreDropped()
    {
        var data = Clean(new WGAdminObserverData
        {
            DefaultPreset = "Gone",
            Presets = new()
            {
                new WGAvatarPreset { Name = "Gone", Kind = WGAvatarKind.Object, Prototype = "Removed" },
                new WGAvatarPreset { Name = "Kept", Kind = WGAvatarKind.Object, Prototype = "Crate" },
                new WGAvatarPreset { Name = "Me", Kind = WGAvatarKind.Character, Outfit = "RemovedGear" },
            },
            Kit = new WGKitLayout
            {
                Slots = new()
                {
                    ["back"] = Node("Bag", At(0, 0, 0, Node("Removed", At(0, 0, 0, Node("Wrench")))), At(2, 0, 0, Node("Wrench"))),
                    ["belt"] = Node("Removed"),
                },
            },
        });

        Assert.Multiple(() =>
        {
            Assert.That(data.Presets.Select(p => p.Name), Is.EqualTo(new[] { "Kept", "Me" }));
            Assert.That(data.DefaultPreset, Is.Null, "A default that names a dropped preset should clear.");
            Assert.That(data.Presets[1].Outfit, Is.Null, "A missing outfit should clear, keeping the character.");
            Assert.That(data.Kit!.Slots.Keys, Is.EqualTo(new[] { "back" }));
            Assert.That(data.Kit.Slots["back"].Contents.Select(c => c.Node.Prototype), Is.EqualTo(new[] { "Wrench" }));
        });
    }

    [Test]
    public void LimitsHold()
    {
        var deep = Node("Bag", At(0, 0, 0, Node("Bag", At(0, 0, 0, Node("Bag", At(0, 0, 0, Node("Wrench")))))));
        var wide = Node("Bag", Enumerable.Range(0, 300).Select(i => At(i, 0, 0, Node("Wrench"))).ToArray());
        var data = Clean(new WGAdminObserverData
        {
            Presets = Enumerable.Range(0, 50)
                .Select(i => new WGAvatarPreset { Name = $"Preset {i}", DisplayName = new string('x', 500) })
                .Append(new WGAvatarPreset { Name = "Preset 0" })
                .ToList(),
            Marker = new WGStaffMarkerSettings { Visuals = WGMarkerVisuals.None, Color = "pink" },
            Kit = new WGKitLayout { Slots = new() { ["back"] = deep, ["belt"] = wide } },
        });

        Assert.Multiple(() =>
        {
            Assert.That(data.Presets, Has.Count.EqualTo(WGAdminObserverData.MaxPresets));
            Assert.That(data.Presets[0].DisplayName, Has.Length.EqualTo(WGAdminObserverData.MaxDisplayNameLength));
            Assert.That(data.Marker.Visuals, Is.EqualTo(WGMarkerVisuals.Nameplate), "A marker always has a visual.");
            Assert.That(data.Marker.Color, Is.Null, "A colour that is not hex should clear.");

            var third = data.Kit!.Slots["back"].Contents.Single().Node.Contents.Single().Node;
            Assert.That(third.Contents, Is.Empty, "Nothing is kept below the third level.");
            Assert.That(Count(data.Kit), Is.EqualTo(WGAdminObserverData.MaxKitNodes));
        });
    }

    [Test]
    public void OversizedDocumentIsRefused()
    {
        var data = new WGAdminObserverData
        {
            Presets = Enumerable.Range(0, WGAdminObserverData.MaxPresets)
                .Select(i => new WGAvatarPreset
                {
                    Name = $"Preset {i}",
                    Description = new string('x', WGAdminObserverData.MaxDescriptionLength),
                    OutfitLayout = new WGKitLayout
                    {
                        Slots = new()
                        {
                            ["back"] = Node("Bag", Enumerable.Range(0, 150).Select(n => At(n, 0, 0, Node("Wrench"))).ToArray()),
                        },
                    },
                    Kind = WGAvatarKind.Character,
                })
                .ToList(),
        };

        Assert.That(WGAdminObserverSerializer.TrySerialize(Clean(data), out _), Is.False);
    }

    [Test]
    public void PresetNameIsTrimmedAndCut()
    {
        var max = WGAdminObserverData.MaxNameLength;
        var cut = new string('a', max - 1) + " b";
        Assert.Multiple(() =>
        {
            Assert.That(WGAdminObserverData.PresetName("  Dog  "), Is.EqualTo("Dog"));
            Assert.That(WGAdminObserverData.PresetName(new string('a', max + 5)), Is.EqualTo(new string('a', max)));
            Assert.That(WGAdminObserverData.PresetName(cut), Is.EqualTo(new string('a', max - 1)), "A cut that ends in a space is trimmed again.");
        });
    }

    private static int Count(WGKitLayout layout)
    {
        return layout.Slots.Values.Concat(layout.Hands.Values).Sum(Count);
    }

    private static int Count(WGKitNode node)
    {
        return 1 + node.Contents.Sum(c => Count(c.Node));
    }
}
