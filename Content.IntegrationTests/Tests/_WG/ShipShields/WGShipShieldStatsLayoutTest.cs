using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WG.CombatConsole;
using Content.Client._WG.ShipShields;
using Content.Client._WG.Stylesheets;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.ShipShields;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks generator specifications fit a compact popup without expanding helm controls.</summary>
[TestFixture]
public sealed class WGShipShieldStatsLayoutTest
{
    [Test]
    public async Task SpecificationsFitSmallPanelAndUnavailableGeneratorsDisableAccess()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        await pair.Client.WaitAssertion(() =>
        {
            var style = pair.Client.ResolveDependency<IUserInterfaceManager>().Stylesheet;
            var stats = new WGShipShieldGeneratorStats("[color=red]Long renamed generator[/color] MS-100", 50000f,
                70000f, 1000f, 6000f, 40000f, 190000f, 60f);
            Assert.That(WGInstrumentTheme.Skin, Is.SameAs(WolfgateSkins.Get(pair.Client.ResolveDependency<IConfigurationManager>().GetCVar(WolfgateCVars.UiStyle))),
                "Themed controls must build outside a session.");
            using var window = new WGShipShieldStatsWindow { Stylesheet = style };
            window.UpdateStats(stats);
            Assert.That(HasGlass(window), Is.True, "Instrument consoles show the specifications on glass readouts.");
            var name = (RichTextLabel) typeof(WGShipShieldStatsWindow).GetField("_name", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.That(name.Text, Is.EqualTo(FormattedMessage.FromUnformatted(Loc.GetString("wg-shield-stats-generator", ("name", stats.Name))).ToMarkup()),
                "Renamed generators must display markup characters literally.");
            foreach (var size in new[] { new Vector2(340f, 300f), new Vector2(440f, 420f) })
            {
                window.SetSize = size;
                window.Measure(size);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                Assert.That(window.IsArrangeValid, Is.True);
                Assert.That(window.DesiredSize.X, Is.LessThanOrEqualTo(size.X));
                AssertBounds(window);
            }
            using var plain = new WGShipShieldStatsWindow(false) { Stylesheet = style };
            plain.UpdateStats(stats);
            Assert.That(HasGlass(plain), Is.False, "The plain generator panel keeps the stock window chrome.");
            Assert.That(plain.Children.OfType<WGConsoleThemeBinding>(), Is.Empty);
            foreach (var size in new[] { new Vector2(340f, 300f), new Vector2(440f, 420f) })
            {
                plain.SetSize = size;
                plain.Measure(size);
                plain.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                Assert.That(plain.IsArrangeValid, Is.True);
                AssertBounds(plain);
            }
            using var panel = new WGShipShieldShuntScreen { Stylesheet = style };
            var button = (Button) typeof(WGShipShieldShuntScreen).GetField("_stats", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
            Assert.That(button.Disabled, Is.True);
            panel.UpdateState(new WGShipShieldShuntState(true, true, 1f, 0f, 0f, MathF.PI / 2f) { Stats = stats }, 0f);
            Assert.That(button.Disabled, Is.False);
            var panelSize = new Vector2(760f, 540f);
            panel.Measure(panelSize);
            panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, panelSize));
            Assert.That(panel.DesiredSize.X, Is.LessThanOrEqualTo(panelSize.X));
            AssertBounds(panel);
            panel.UpdateState(null, 0f);
            Assert.That(button.Disabled, Is.True);
        });
        await pair.CleanReturnAsync();
    }

    private static bool HasGlass(Control parent) => parent is WGGlassReadout || parent.Children.Any(HasGlass);

    private static void AssertBounds(Control parent)
    {
        if (parent is Slider)
            return;
        foreach (var child in parent.Children)
        {
            if (!child.Visible)
                continue;
            Assert.That(child.Size.X, Is.LessThanOrEqualTo(parent.Size.X + 1f));
            AssertBounds(child);
        }
    }
}
