#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Loadouts;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Loadouts;

/// <summary>
/// The loadout window driven the way the character editor drives it: every selection goes out through the
/// window's events, the loadout is changed, and the window is told to refresh.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfgateLoadoutWindow))]
public sealed class LoadoutWindowInteractionTest : InteractionTest
{
    private const string Role = "JobContractor";

    private WolfgateLoadoutWindow _window = default!;
    private RoleLoadout _loadout = default!;

    private async Task Open()
    {
        var proto = Client.ResolveDependency<IPrototypeManager>();
        var collection = Client.ResolveDependency<IDependencyCollection>();
        var session = Client.ResolveDependency<IPlayerManager>().LocalSession!;

        await Client.WaitPost(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
            _loadout = new RoleLoadout(Role);
            _loadout.SetDefault(profile, session, proto);

            _window = new WolfgateLoadoutWindow(profile, _loadout, proto.Index<RoleLoadoutPrototype>(Role), session, collection);
            _window.RefreshLoadouts(_loadout, session, collection);
            _window.OpenCenteredLeft();

            _window.OnLoadoutPressed += (group, loadout) =>
            {
                _loadout.AddLoadout(group, loadout, proto);
                _window.RefreshLoadouts(_loadout, session, collection);
            };
            _window.OnLoadoutUnpressed += (group, loadout) =>
            {
                _loadout.RemoveLoadout(group, loadout, proto);
                _window.RefreshLoadouts(_loadout, session, collection);
            };
        });
        await RunTicks(6);
    }

    private List<string> Held(LoadoutGroupView group)
    {
        return _loadout.SelectedLoadouts[group.Id].Select(l => l.Prototype.Id).ToList();
    }

    private List<LoadoutTile> Tiles()
    {
        return Descendants(_window).OfType<LoadoutTile>().Where(t => t.Visible).ToList();
    }

    /// <summary>Every control of the rail and the grid: what the player is looking at while picking.</summary>
    private List<Control> Browsing()
    {
        return Descendants(_window.FindControl<Control>("Rail"))
            .Concat(Descendants(_window.FindControl<Control>("Grid")))
            .ToList();
    }

    /// <summary>A click swaps the pick of a single-pick group without rebuilding or scrolling anything.</summary>
    [Test]
    public async Task ClickSwapsThePickInPlace()
    {
        await Open();

        LoadoutGroupView group = default!;
        LoadoutTile tile = default!;
        List<Control> before = default!;
        var scroll = 0f;

        await Client.WaitPost(() =>
        {
            // Long enough to scroll: the test is about the list staying where the player scrolled it to.
            group = _window.Catalog.Groups.First(g => g.Single && g.Entries.Count(e => !e.Locked) >= 60);
            _window.OpenGroup(group);
        });
        await RunTicks(6);

        await Client.WaitPost(() =>
        {
            var grid = _window.FindControl<LoadoutTileGrid>("Grid");
            _window.FindControl<ScrollContainer>("GridScroll").SetScrollValue(new Vector2(0, 2 * grid.RowHeight));
        });
        await RunTicks(3);

        await Client.WaitAssertion(() =>
        {
            scroll = _window.FindControl<ScrollContainer>("GridScroll").GetScrollValue().Y;
            Assert.That(scroll, Is.GreaterThan(0), "the list is scrolled before the click");

            tile = Tiles().First(t => t.Entry is { Selected: false, Locked: false });
            before = Browsing();
        });

        var picked = tile.Entry!;
        await ClickControl(tile);
        await RunTicks(3);

        await Client.WaitAssertion(() =>
        {
            Assert.That(Held(group), Is.EqualTo(new[] { picked.Id.Id }), "the click replaced the group's pick");
            Assert.That(tile.Entry, Is.SameAs(picked), "the tile still shows the same loadout");
            Assert.That(tile.Pressed, Is.True, "the picked tile is lit");
            Assert.That(Tiles().Count(t => t.Pressed), Is.EqualTo(1), "and it is the only one lit");
            Assert.That(Browsing(), Is.EqualTo(before), "no rail row or tile was rebuilt");
            Assert.That(_window.FindControl<ScrollContainer>("GridScroll").GetScrollValue().Y, Is.EqualTo(scroll), "the grid did not scroll");

            var row = Descendants(_window).OfType<LoadoutRailRow>().Single(r => r.Group == group);
            Assert.That(Descendants(row).OfType<Label>().Select(l => l.Text), Does.Contain(picked.Name), "the rail names the new pick");
            _window.Dispose();
        });
    }

    /// <summary>The limits of a group are kept: a required pick can't be dropped and a full group doesn't push a pick out.</summary>
    [Test]
    public async Task LimitsRefuseInsteadOfSilentlyChanging()
    {
        await Open();

        await Client.WaitAssertion(() =>
        {
            var required = _window.Catalog.Groups.First(g => g.Single && !g.Optional && g.Picks.Count == 1);
            var held = Held(required);
            Assert.That(_window.TrySelect(required.Picks[0]), Is.False, "a required pick can only be replaced");
            Assert.That(Held(required), Is.EqualTo(held));

            var multi = _window.Catalog.Groups.First(g => !g.Single && g.Entries.Count(e => !e.Locked) > g.Max);
            foreach (var pick in multi.Picks.ToList())
            {
                var removable = multi.Picks.Count > multi.Min;
                Assert.That(_window.TrySelect(pick), Is.EqualTo(removable));
            }

            var open = multi.Entries.Where(e => !e.Locked).ToList();
            for (var i = 0; i < multi.Max; i++)
            {
                Assert.That(_window.TrySelect(open[i]), Is.True, $"pick {i + 1} of {multi.Max}");
            }

            held = Held(multi);
            Assert.That(held, Has.Count.EqualTo(multi.Max));
            Assert.That(_window.TrySelect(open[multi.Max]), Is.False, "a full group takes no more");
            Assert.That(Held(multi), Is.EqualTo(held), "and keeps the picks it had");

            Assert.That(_window.TrySelect(open[0]), Is.True, "a pick above the minimum can be dropped");
            Assert.That(Held(multi), Has.Count.EqualTo(multi.Max - 1));
            _window.Dispose();
        });
    }

    /// <summary>
    /// With nothing hovered the detail strip shows the open group's picks: one is described in full, several
    /// share the strip as cards that fill it, with nothing to scroll.
    /// </summary>
    [Test]
    public async Task PicksFillTheDetailStrip()
    {
        await Open();

        LoadoutGroupView group = default!;
        await Client.WaitPost(() =>
        {
            group = _window.Catalog.Groups.First(g => !g.Single && g.Max >= 3 && g.Entries.Count(e => !e.Locked) >= g.Max);
            _window.OpenGroup(group);
            foreach (var pick in group.Picks.ToList())
            {
                _window.TrySelect(pick);
            }

            _window.TrySelect(group.Entries.First(e => !e.Locked));
        });
        await RunTicks(4);

        List<Control> Cards(LoadoutDetailStrip strip)
        {
            return Descendants(strip).Where(c => c.Visible && c.HasStyleClass("LoadoutPick")).ToList();
        }

        await Client.WaitAssertion(() =>
        {
            var strip = Descendants(_window).OfType<LoadoutDetailStrip>().Single();
            Assert.That(Held(group), Has.Count.EqualTo(1));
            Assert.That(Cards(strip), Is.Empty, "a single pick is described, not carded");
            Assert.That(Descendants(strip).OfType<Label>().Where(l => l.Visible).Select(l => l.Text), Does.Contain(group.Picks[0].Name));

            foreach (var entry in group.Entries.Where(e => !e.Locked && !e.Selected).Take(group.Max - 1))
            {
                _window.TrySelect(entry);
            }
        });
        await RunTicks(4);

        await Client.WaitAssertion(() =>
        {
            var strip = Descendants(_window).OfType<LoadoutDetailStrip>().Single();
            var cards = Cards(strip);
            Assert.That(Held(group), Has.Count.EqualTo(group.Max));
            Assert.That(cards, Has.Count.EqualTo(group.Max), "one card per pick");
            Assert.That(Descendants(strip).OfType<ScrollContainer>(), Is.Empty, "the picks don't scroll");

            // Together the cards cover the strip: its whole width on every row, and its whole height.
            var left = cards.Min(c => c.GlobalPosition.X);
            var right = cards.Max(c => c.GlobalPosition.X + c.Size.X);
            var top = cards.Min(c => c.GlobalPosition.Y);
            var bottom = cards.Max(c => c.GlobalPosition.Y + c.Size.Y);
            Assert.That(right - left, Is.GreaterThan(strip.Size.X - 12), "the cards span the strip's width");
            Assert.That(bottom - top, Is.GreaterThan(strip.Size.Y - 12), "the cards span the strip's height");

            foreach (var card in cards)
            {
                Assert.That(card.Size.X, Is.GreaterThan(120), "a card is wide enough to read");
                Assert.That(card.GlobalPosition.X + card.Size.X, Is.LessThanOrEqualTo(strip.GlobalPosition.X + strip.Size.X + 0.5f), "no card runs out of the strip");
                Assert.That(card.GlobalPosition.Y + card.Size.Y, Is.LessThanOrEqualTo(strip.GlobalPosition.Y + strip.Size.Y + 0.5f));
            }

            _window.Dispose();
        });
    }

    /// <summary>An optional single-pick group lists a "Nothing" tile that clears it.</summary>
    [Test]
    public async Task NothingTileClearsAnOptionalGroup()
    {
        await Open();

        LoadoutGroupView group = default!;
        await Client.WaitPost(() =>
        {
            group = _window.Catalog.Groups.First(g => g.HasNothingTile && g.Entries.Any(e => !e.Locked));
            _window.OpenGroup(group);
            if (group.Picks.Count == 0)
                _window.TrySelect(group.Entries.First(e => !e.Locked));
        });
        await RunTicks(6);

        LoadoutTile nothing = default!;
        await Client.WaitAssertion(() =>
        {
            Assert.That(Held(group), Has.Count.EqualTo(1));
            nothing = Tiles().Single(t => t.NothingFor == group);
            Assert.That(nothing.Pressed, Is.False);
        });

        await ClickControl(nothing);
        await RunTicks(3);

        await Client.WaitAssertion(() =>
        {
            Assert.That(Held(group), Is.Empty, "the Nothing tile cleared the group");
            Assert.That(nothing.Pressed, Is.True, "and is lit now");
            _window.Dispose();
        });
    }

    /// <summary>A search lists matches from every group and going back restores the group that was open.</summary>
    [Test]
    public async Task SearchListsEveryGroupAndReturns()
    {
        await Open();

        LoadoutGroupView group = default!;
        LoadoutEntry target = default!;
        await Client.WaitPost(() =>
        {
            group = _window.OpenedGroup!;

            // Something from another group, found by the name of that group.
            var other = _window.Catalog.Groups.Last(g => g != group && g.Entries.Any(e => !e.Locked));
            target = other.Entries.First(e => !e.Locked);
            _window.SetQuery(other.Label);
        });
        await RunTicks(6);

        await Client.WaitAssertion(() =>
        {
            var tiles = Tiles();
            Assert.That(tiles, Is.Not.Empty);
            Assert.That(tiles.Select(t => t.Entry?.Group), Does.Contain(target.Group), "matches come from other groups");
            Assert.That(tiles.All(t => t.NothingFor == null), Is.True, "search results hold no Nothing tile");
            Assert.That(tiles.All(t => t.Entry!.SearchText.Contains(target.Group.Label.ToLowerInvariant())), Is.True);
            Assert.That(Descendants(_window).OfType<LoadoutRailRow>().Count(r => r.Pressed), Is.Zero, "no rail row is lit during a search");
            Assert.That(_window.OpenedGroup, Is.SameAs(group), "the open group is kept to return to");
        });

        // A rail row clicked during a search ends it and opens that row's group.
        var row = Descendants(_window).OfType<LoadoutRailRow>().Single(r => r.Group == target.Group);
        await ClickControl(row);
        await RunTicks(4);

        await Client.WaitAssertion(() =>
        {
            Assert.That(_window.OpenedGroup, Is.SameAs(target.Group), "the row opened its group");
            Assert.That(_window.FindControl<LineEdit>("SearchBar").Text, Is.Empty, "and ended the search");
            Assert.That(row.Pressed, Is.True);
            Assert.That(Tiles().Where(t => t.Entry != null).All(t => t.Entry!.Group == target.Group), Is.True);

            group = target.Group;
            _window.SetQuery("zzzz no such thing");
        });
        await RunTicks(3);

        await Client.WaitAssertion(() =>
        {
            Assert.That(Tiles(), Is.Empty, "nothing matches");
            Assert.That(_window.FindControl<Control>("EmptyBox").Visible, Is.True, "the empty state shows");
            _window.SetQuery(string.Empty);
        });
        await RunTicks(3);

        await Client.WaitAssertion(() =>
        {
            Assert.That(Tiles().Where(t => t.Entry != null).All(t => t.Entry!.Group == group), Is.True, "the grid is back on the open group");
            Assert.That(Descendants(_window).OfType<LoadoutRailRow>().Single(r => r.Pressed).Group, Is.SameAs(group));
            _window.Dispose();
        });
    }

    /// <summary>
    /// Nothing is squeezed out of the window at the size it opens at, and browsing the biggest role's groups
    /// keeps the number of preview entities bounded.
    /// </summary>
    [Test]
    [TestCase(1f)]
    [TestCase(1.25f)]
    public async Task FitsTheWindowAndBoundsItsEntities(float uiScale)
    {
        var config = Client.ResolveDependency<IConfigurationManager>();
        var original = config.GetCVar(CVars.DisplayUIScale);
        await Client.WaitPost(() => config.SetCVar(CVars.DisplayUIScale, uiScale));

        await Open();

        await Client.WaitAssertion(() =>
        {
            var parent = _window.Parent!;
            Assert.That(_window.Size.X, Is.LessThanOrEqualTo(parent.Size.X + 0.5f), "the window is no wider than the screen");

            var detail = Descendants(_window).OfType<LoadoutDetailStrip>().Single();
            var bottom = _window.GlobalPosition.Y + _window.Size.Y;
            Assert.That(detail.Size.Y, Is.EqualTo(LoadoutDetailStrip.StripHeight).Within(0.5f), "the detail strip keeps its height");
            Assert.That(detail.GlobalPosition.Y + detail.Size.Y, Is.LessThanOrEqualTo(bottom + 0.5f), "and sits inside the window");

            var tiles = Tiles();
            Assert.That(tiles, Is.Not.Empty);
            foreach (var tile in tiles.Take(4))
            {
                Assert.That(tile.Size.X, Is.GreaterThanOrEqualTo(LoadoutTile.TileWidth - 0.5f), "tiles keep their width");
            }
        });

        foreach (var group in _window.Catalog.Groups.ToList())
        {
            await Client.WaitPost(() => _window.OpenGroup(group));
            await RunTicks(4);
            await Client.WaitAssertion(() =>
            {
                Assert.That(Tiles(), Is.Not.Empty, $"{group.Id} shows no tile");
                Assert.That(_window.IconCount, Is.LessThan(200), $"too many preview entities after opening {group.Id}");
            });
        }

        await Client.WaitPost(() =>
        {
            _window.Dispose();
            config.SetCVar(CVars.DisplayUIScale, original);
        });
        await RunTicks(2);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;

            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
