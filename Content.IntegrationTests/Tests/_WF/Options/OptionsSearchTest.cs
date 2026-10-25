#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Options;
using Content.Client.Options.UI;
using Content.Client.Options.UI.Tabs;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.Options;

/// <summary>The options window search filters settings across tabs and hides tabs with no match.</summary>
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
            var tabs = menu.Tabs;
            var search = new OptionsSearch(tabs);

            var graphics = tabs.GetChild(1);
            var keys = (KeyRebindTab) tabs.GetChild(2);
            var vsync = graphics.FindControl<CheckBox>("VSyncCheckBox");
            var fullscreen = graphics.FindControl<CheckBox>("FullscreenCheckBox");
            var display = Find<Label>(graphics, l => l.Text == loc.GetString("ui-options-display-label"));
            var quality = Find<Label>(graphics, l => l.Text == loc.GetString("ui-options-quality-label"));

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
            });

            // A header match keeps its whole section.
            search.Filter(loc.GetString("ui-options-display-label"));
            Assert.That(fullscreen.Visible, Is.True);

            // Keybinds go through the Controls tab's own filter.
            search.Filter(loc.GetString("ui-options-function-zoom-in"));
            Assert.Multiple(() =>
            {
                Assert.That(tabs.GetTabVisible(2), Is.True);
                Assert.That(tabs.CurrentTab, Is.EqualTo(2));
                Assert.That(vsync.Visible, Is.False);
            });

            Assert.That(search.Filter("zzzz no such setting"), Is.False);
            Assert.That(Enumerable.Range(0, tabs.ChildCount).Where(i => i != 5).All(tabs.GetTabVisible), Is.True);

            // The tab strip has to be rebuilt when the query is cleared, or it keeps showing only the matched tabs.
            search.Filter(loc.GetString("ui-options-vsync"));
            tabs.Measure(new Vector2(800, 450));
            search.Filter(" ");
            Assert.That(tabs.IsMeasureValid, Is.False);

            var binds = keys.FindControl<BoxContainer>("KeybindsContainer");
            Assert.Multiple(() =>
            {
                Assert.That(vsync.Visible, Is.True);
                Assert.That(fullscreen.Visible, Is.True);
                Assert.That(quality.Visible, Is.True);
                Assert.That(binds.Children.All(c => c.Visible), Is.True);
            });
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
