#nullable enable
using System.Linq;
using Content.Client._WF.Options;
using Content.Client.Options.UI.Tabs;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.Options;

/// <summary>The Controls tab search hides rows and headers that don't match.</summary>
[TestFixture]
[TestOf(typeof(KeybindSearch))]
public sealed class KeybindSearchTest
{
    [Test]
    public async Task SearchFiltersRows()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var loc = client.ResolveDependency<ILocalizationManager>();

        await client.WaitAssertion(() =>
        {
            var tab = new KeyRebindTab();
            var container = tab.FindControl<BoxContainer>("KeybindsContainer");
            var children = container.Children.ToList();

            Control Header(string id) => children.OfType<Label>().Single(l => l.Text == loc.GetString(id));
            Control Row(string id) => children.Single(c => c is not Label && c.ChildCount > 0 && HasLabel(c, loc.GetString(id)));

            var moveUp = Row("ui-options-function-move-up");
            var zoomIn = Row("ui-options-function-zoom-in");
            var movement = Header("ui-options-header-movement");
            var camera = Header("ui-options-header-camera");

            KeybindSearch.Filter(container, "ZOOM in");
            Assert.Multiple(() =>
            {
                Assert.That(zoomIn.Visible, Is.True);
                Assert.That(camera.Visible, Is.True);
                Assert.That(moveUp.Visible, Is.False);
                Assert.That(movement.Visible, Is.False);
            });

            // Headless clients have no key names, so give the first bind button one.
            FirstButton(moveUp)!.Text = "W";
            KeybindSearch.Filter(container, "w");
            Assert.Multiple(() =>
            {
                Assert.That(moveUp.Visible, Is.True);
                Assert.That(zoomIn.Visible, Is.False);
            });

            KeybindSearch.Filter(container, " ");
            Assert.That(children.All(c => c.Visible), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    private static Button? FirstButton(Control control)
    {
        return control as Button ?? control.Children.Select(FirstButton).FirstOrDefault(b => b != null);
    }

    private static bool HasLabel(Control control, string text)
    {
        if (control is Label label && label.Text == text)
            return true;

        return control.Children.Any(c => HasLabel(c, text));
    }
}
