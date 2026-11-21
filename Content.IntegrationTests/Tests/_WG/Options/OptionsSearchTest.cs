#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WG.Options;
using Content.Client._WG.Options.UI;
using Content.Client._WG.Options.UI.Tabs;
using Content.Client.Options.UI;
using Content.Client.Options.UI.Tabs;
using Content.IntegrationTests.Tests._WG.EngineCompat;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WG.Options;

/// <summary>
/// The options window search filters settings across tabs and hides tabs with no match, on both the SS14 and the
/// Wolfgate master tab, moving to whichever has the match.
/// </summary>
[TestFixture]
[TestOf(typeof(OptionsSearch))]
public sealed class OptionsSearchTest
{
    [Test]
    public async Task SearchFiltersTabs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var loc = client.ResolveDependency<ILocalizationManager>();

        await client.WaitAssertion(() =>
        {
            var menu = new OptionsMenu();
            var master = menu.FindControl<TabContainer>("WgMaster");
            var tabs = menu.Tabs;
            var wolfgate = menu.FindControl<WolfgateOptionsTabs>("WolfgateTabs");
            var search = new OptionsSearch(master);

            var graphics = tabs.GetChild(1);
            var keys = (KeyRebindTab) tabs.GetChild(2);
            var vsync = graphics.FindControl<CheckBox>("VSyncCheckBox");
            var fullscreen = graphics.FindControl<CheckBox>("FullscreenCheckBox");
            var display = Find<Label>(graphics, l => l.Text == loc.GetString("ui-options-display-label"));
            var quality = Find<Label>(graphics, l => l.Text == loc.GetString("ui-options-quality-label"));
            var wgInterface = OptionsTabTestHelper.Find<WGInterfaceTab>(menu);
            var screentips = wgInterface.FindControl<OptionDropDown>("DropDownScreentips");
            var outline = wgInterface.FindControl<CheckBox>("OutlineCheckBox");

            // Unique to the Graphics tab's VSync checkbox.
            Assert.That(search.Filter(loc.GetString("ui-options-vsync")), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(vsync.Visible, Is.True);
                Assert.That(display.Visible, Is.True);
                Assert.That(fullscreen.Visible, Is.False);
                Assert.That(quality.Visible, Is.False);
                Assert.That(tabs.GetTabVisible(1), Is.True);
                Assert.That(tabs.GetTabVisible(0), Is.False);
                Assert.That(tabs.CurrentTab, Is.EqualTo(1));
                Assert.That(master.CurrentTab, Is.EqualTo(OptionsMenu.WgStockPage));
                Assert.That(master.GetTabVisible(OptionsMenu.WgWolfgatePage), Is.False);
            });

            // A header match keeps its whole section.
            search.Filter(loc.GetString("ui-options-display-label"));
            Assert.That(fullscreen.Visible, Is.True);

            // A Wolfgate-only setting, searched from the SS14 side, moves to the Wolfgate master tab and its tab.
            search.Filter(loc.GetString("ui-options-wg-screentips"));
            Assert.Multiple(() =>
            {
                Assert.That(master.CurrentTab, Is.EqualTo(OptionsMenu.WgWolfgatePage));
                Assert.That(master.GetTabVisible(OptionsMenu.WgStockPage), Is.False);
                Assert.That(wolfgate.CurrentTab, Is.EqualTo(0));
                Assert.That(screentips.Visible, Is.True);
                Assert.That(outline.Visible, Is.False);
            });

            // The strip disables the side with no match and counts the matching tabs on the other.
            var strip = menu.FindControl<WGOptionsMasterStrip>("WgMasterStrip");
            strip.Refresh(true);
            var buttons = strip.GetChild(0).Children.OfType<Button>().ToList();
            Assert.Multiple(() =>
            {
                Assert.That(buttons[OptionsMenu.WgStockPage].Disabled, Is.True);
                Assert.That(buttons[OptionsMenu.WgWolfgatePage].Pressed, Is.True);
                Assert.That(buttons[OptionsMenu.WgWolfgatePage].Text, Is.EqualTo(loc.GetString("wg-options-master-tab-matches",
                    ("title", loc.GetString("wg-options-master-wolfgate")), ("count", 1))));
            });

            // Fork keybinds are on the Wolfgate Controls tab.
            search.Filter(loc.GetString("ui-options-function-toggle-standing"));
            Assert.Multiple(() =>
            {
                Assert.That(master.CurrentTab, Is.EqualTo(OptionsMenu.WgWolfgatePage));
                Assert.That(wolfgate.CurrentTab, Is.EqualTo(WolfgateOptionsTabs.ControlsTabIndex));
            });

            // Stock keybinds go through the SS14 Controls tab's own filter, back on the SS14 side.
            search.Filter(loc.GetString("ui-options-function-zoom-in"));
            Assert.Multiple(() =>
            {
                Assert.That(master.CurrentTab, Is.EqualTo(OptionsMenu.WgStockPage));
                Assert.That(tabs.GetTabVisible(2), Is.True);
                Assert.That(tabs.CurrentTab, Is.EqualTo(2));
                Assert.That(vsync.Visible, Is.False);
            });

            // A match on both sides keeps the current one and leaves the other selectable.
            search.Filter(loc.GetString("ui-options-volume-label"));
            Assert.Multiple(() =>
            {
                Assert.That(master.CurrentTab, Is.EqualTo(OptionsMenu.WgStockPage));
                Assert.That(master.GetTabVisible(OptionsMenu.WgWolfgatePage), Is.True);
                Assert.That(tabs.CurrentTab, Is.EqualTo(3));
            });

            Assert.That(search.Filter("zzzz no such setting"), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(Enumerable.Range(0, tabs.ChildCount).Where(i => i != 5).All(tabs.GetTabVisible), Is.True);
                Assert.That(Enumerable.Range(0, master.ChildCount).All(master.GetTabVisible), Is.True);
                Assert.That(Enumerable.Range(0, wolfgate.ChildCount).All(wolfgate.GetTabVisible), Is.True);
            });

            // The tab strips have to be rebuilt when the query is cleared, or they keep showing only the matched tabs.
            search.Filter(loc.GetString("ui-options-vsync"));
            tabs.Measure(new Vector2(800, 450));
            wolfgate.Measure(new Vector2(800, 450));
            search.Filter(" ");
            Assert.Multiple(() =>
            {
                Assert.That(tabs.IsMeasureValid, Is.False);
                Assert.That(wolfgate.IsMeasureValid, Is.False);
            });

            var binds = keys.FindControl<BoxContainer>("KeybindsContainer");
            var forkBinds = wolfgate.ControlsTab.FindControl<BoxContainer>("KeybindsContainer");
            Assert.Multiple(() =>
            {
                Assert.That(vsync.Visible, Is.True);
                Assert.That(fullscreen.Visible, Is.True);
                Assert.That(quality.Visible, Is.True);
                Assert.That(outline.Visible, Is.True);
                Assert.That(binds.Children.All(c => c.Visible), Is.True);
                Assert.That(forkBinds.Children.All(c => c.Visible), Is.True);
                Assert.That(Enumerable.Range(0, wolfgate.ChildCount).All(wolfgate.GetTabVisible), Is.True);
            });

            menu.Teardown();
        });

        await pair.CleanReturnAsync();
    }

    private static T Find<T>(Control control, Func<T, bool> predicate) where T : Control
    {
        return All(control).OfType<T>().First(predicate);
    }

    private static IEnumerable<Control> All(Control control)
    {
        yield return control;
        foreach (var child in control.Children)
        {
            foreach (var descendant in All(child))
                yield return descendant;
        }
    }
}
