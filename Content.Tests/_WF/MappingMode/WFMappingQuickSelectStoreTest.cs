#nullable enable
using System.Linq;
using System.Text;
using Content.Client._WF.MappingMode.Palette;
using Content.Client._WF.MappingMode.QuickSelect;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.MappingMode;

[TestFixture]
public sealed class WFMappingQuickSelectStoreTest
{
    private static WFMappingPaletteEntry Entity(string id) => new(WFMappingPaletteKind.Entity, id);

    private static WFMappingPaletteEntry Tile(string id) => new(WFMappingPaletteKind.Tile, id);

    private const string ActionsSample = @"
- action: !type:InstantActionComponent
    event: !type:StartPlacementActionEvent
      placementOption: AlignTileAny
      tileId: Space
  name: space
- action: !type:InstantActionComponent
    event: !type:StartPlacementActionEvent
      eraser: True
  name: erase
- action: !type:InstantActionComponent
    event: !type:ToggleMappingToolsActionEvent
  name: tools
- action: !type:InstantActionComponent
    event: !type:StartPlacementActionEvent
      placementOption: SnapgridCenter
      entityType: WallSolid
  name: wall
- action: !type:InstantActionComponent
    event: !type:StartPlacementActionEvent
      placementOption: AlignTileAny
      tileId: FloorSteel
  name: steel
- action: !type:InstantActionComponent
    event: !type:StartPlacementActionEvent
      placementOption: SnapgridCenter
      entityType: WallSolid
  name: wall again
- action: !type:InstantActionComponent
    icon: Interface/VerbIcons/delete.svg.192dpi.png
  name: no event
";

    [Test]
    public void DefaultsComeFromTheActionsFile()
    {
        var entries = WFMappingQuickSelectStore.ParseActions(ActionsSample);
        Assert.That(entries, Is.EqualTo(new[] { Tile("Space"), Entity("WallSolid"), Tile("FloorSteel") }),
            "File order, no eraser, no toggle, no repeats.");

        var store = WFMappingQuickSelectStore.Create("Defaults", entries);
        Assert.Multiple(() =>
        {
            Assert.That(store.Sets, Has.Count.EqualTo(1));
            Assert.That(store.Current.Name, Is.EqualTo("Defaults"));
            Assert.That(store.Current.Pages, Has.Count.EqualTo(1));
            Assert.That(store.CurrentPage[0]?.Entry, Is.EqualTo(Tile("Space")));
            Assert.That(store.CurrentPage[1]?.Entry, Is.EqualTo(Entity("WallSolid")));
            Assert.That(store.CurrentPage[2]?.Entry, Is.EqualTo(Tile("FloorSteel")));
            Assert.That(store.CurrentPage.Skip(3), Is.All.Null);
        });
    }

    [Test]
    public void DefaultsFillPagesOfTen()
    {
        var entries = Enumerable.Range(0, 25).Select(i => Entity($"E{i}")).ToList();
        var store = WFMappingQuickSelectStore.Create("Many", entries);
        Assert.Multiple(() =>
        {
            Assert.That(store.Current.Pages, Has.Count.EqualTo(3));
            Assert.That(store.Current.Pages[2][4]?.Entry, Is.EqualTo(Entity("E24")));
            Assert.That(store.Current.Pages[2][5], Is.Null);
        });

        var tooMany = Enumerable.Range(0, 150).Select(i => Entity($"E{i}")).ToList();
        Assert.That(WFMappingQuickSelectStore.Create("Lots", tooMany).Current.Pages,
            Has.Count.EqualTo(WFMappingQuickSelectStore.MaxPages));
        Assert.That(WFMappingQuickSelectStore.Create("None", new WFMappingPaletteEntry[0]).Current.Pages, Has.Count.EqualTo(1));
    }

    [TestCase("")]
    [TestCase("not: [closed")]
    [TestCase("- a list\n- not a mapping")]
    [TestCase("version: 1\nactiveSet: 0")]
    [TestCase("sets: []")]
    [TestCase("sets: [1, 2]")]
    [TestCase("sets: hello")]
    public void MalformedTextGivesDefaults(string text)
    {
        Assert.That(WFMappingQuickSelectStore.Read(text), Is.Null);
    }

    [TestCase("")]
    [TestCase("action: [")]
    [TestCase("a: mapping")]
    public void MalformedActionsGiveNothing(string text)
    {
        Assert.That(WFMappingQuickSelectStore.ParseActions(text), Is.Empty);
    }

    [Test]
    public void RoundTrip()
    {
        var store = WFMappingQuickSelectStore.Create("First", new[] { Entity("WallSolid"), Tile("Plating") });
        store.Add(new WFMappingPaletteEntry(WFMappingPaletteKind.Decal, "Arrows", Color.FromHex("#FF000080")), out var added);
        Assert.That(added, Is.True);
        store.SetLabel(0, "  Walls  ");
        Assert.That(store.AddPage(), Is.True);
        store.Add(Entity("Grille"), out _);
        Assert.That(store.AddSet("Second"), Is.True);
        store.Add(Tile("FloorSteel"), out _);
        store.SelectSet(0);
        store.SelectPage(1);
        store.AddPicks = true;

        var read = WFMappingQuickSelectStore.Read(store.Write());
        Assert.That(read, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(read!.Sets.Select(s => s.Name), Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(read.ActiveSet, Is.EqualTo(0));
            Assert.That(read.AddPicks, Is.True);
            Assert.That(read.Current.ActivePage, Is.EqualTo(1));
            var first = read.Sets[0].Pages[0];
            Assert.That(first[0], Is.EqualTo(new WFMappingQuickSlotEntry(Entity("WallSolid"), "Walls")));
            Assert.That(first[1], Is.EqualTo(new WFMappingQuickSlotEntry(Tile("Plating"))));
            Assert.That(first[2]?.Entry.Kind, Is.EqualTo(WFMappingPaletteKind.Decal));
            Assert.That(first[2]?.Entry.Color, Is.EqualTo(Color.FromHex("#FF000080")));
            Assert.That(first[3], Is.Null);
            Assert.That(read.Sets[0].Pages[1][0]?.Entry, Is.EqualTo(Entity("Grille")));
            Assert.That(read.Sets[1].Pages[0][0]?.Entry, Is.EqualTo(Tile("FloorSteel")));
            Assert.That(read.Write(), Is.EqualTo(store.Write()));
        });
    }

    [Test]
    public void UnknownKindsAndBadSlotsAreDropped()
    {
        const string text = @"
activeSet: 7
sets:
- name: Mixed
  activePage: 99
  pages:
  - - kind: entity
      id: WallSolid
    - kind: spaceship
      id: Thing
    - kind: tile
    - {}
    - kind: tile
      id: Plating
";
        var store = WFMappingQuickSelectStore.Read(text);
        Assert.That(store, Is.Not.Null);
        var page = store!.CurrentPage;
        Assert.Multiple(() =>
        {
            Assert.That(store.ActiveSet, Is.EqualTo(0), "An active set past the end is clamped.");
            Assert.That(store.Current.ActivePage, Is.EqualTo(0));
            Assert.That(page[0]?.Entry, Is.EqualTo(Entity("WallSolid")));
            Assert.That(page[1], Is.Null, "Unknown kind.");
            Assert.That(page[2], Is.Null, "No id.");
            Assert.That(page[3], Is.Null);
            Assert.That(page[4]?.Entry, Is.EqualTo(Tile("Plating")), "Slots keep their places.");
        });
    }

    [Test]
    public void ReadCapsSetsPagesSlotsAndNames()
    {
        var builder = new StringBuilder("sets:\n");
        for (var s = 0; s < 15; s++)
        {
            builder.Append($"- name: {new string('n', 50)}{s}\n  pages:\n");
            for (var p = 0; p < 15; p++)
            {
                builder.Append("  -\n");
                for (var i = 0; i < 15; i++)
                {
                    builder.Append($"    - kind: entity\n      id: E{i}\n");
                }
            }
        }

        var store = WFMappingQuickSelectStore.Read(builder.ToString());
        Assert.That(store, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(store!.Sets, Has.Count.EqualTo(WFMappingQuickSelectStore.MaxSets));
            Assert.That(store.Sets.All(s => s.Pages.Count == WFMappingQuickSelectStore.MaxPages), Is.True);
            Assert.That(store.Sets.All(s => s.Pages.All(p => p.Length == WFMappingQuickSelectStore.SlotsPerPage)), Is.True);
            Assert.That(store.Sets[0].Pages[0][9]?.Entry, Is.EqualTo(Entity("E9")));
            Assert.That(store.Sets.All(s => s.Name.Length == WFMappingQuickSelectStore.MaxNameLength), Is.True);
        });
    }

    [Test]
    public void EditingCaps()
    {
        var store = WFMappingQuickSelectStore.Create("One", new WFMappingPaletteEntry[0]);
        for (var i = 1; i < WFMappingQuickSelectStore.MaxSets; i++)
        {
            Assert.That(store.AddSet($"Set {i}"), Is.True);
        }

        Assert.Multiple(() =>
        {
            Assert.That(store.AddSet("Too many"), Is.False);
            Assert.That(store.AddSet("   "), Is.False, "An empty name.");
        });

        for (var i = 1; i < WFMappingQuickSelectStore.MaxPages; i++)
        {
            Assert.That(store.AddPage(), Is.True);
        }

        Assert.That(store.AddPage(), Is.False);

        for (var i = 0; i < WFMappingQuickSelectStore.SlotsPerPage; i++)
        {
            Assert.That(store.Add(Entity($"E{i}"), out var added), Is.EqualTo(i));
            Assert.That(added, Is.True);
        }

        Assert.Multiple(() =>
        {
            Assert.That(store.Add(Entity("E3"), out var again), Is.EqualTo(3), "An entry already there gives its slot.");
            Assert.That(again, Is.False);
            Assert.That(store.Add(Entity("Full"), out _), Is.EqualTo(-1));
            Assert.That(store.Add(Entity(new string('x', WFMappingQuickSelectStore.MaxIdLength + 1)), out _), Is.EqualTo(-1));
            Assert.That(WFMappingQuickSelectStore.NormalizeName(" a\u0001b" + new string('c', 40)),
                Has.Length.EqualTo(WFMappingQuickSelectStore.MaxNameLength).And.StartsWith("ab"));
        });

        while (store.Sets.Count > 1)
        {
            Assert.That(store.DeleteSet(0), Is.True);
        }

        Assert.That(store.DeleteSet(0), Is.False, "The last set stays.");
    }
}
