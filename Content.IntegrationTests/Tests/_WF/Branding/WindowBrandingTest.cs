#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.Branding;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.IntegrationTests.Tests._WF.Branding;

/// <summary>The title-bar badge and spectrum rule fit every header type without crowding it.</summary>
[TestFixture]
public sealed class WindowBrandingTest
{
    [Test]
    public async Task HeadersCarryBrand()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        FancyWindow? plain = null;
        FancyWindow? fancy = null;
        DefaultWindow? @default = null;
        FancyWindow? narrow = null;
        RadarConsoleWindow? radar = null;
        await client.WaitPost(() =>
        {
            plain = Open(new FancyWindow { Title = "Plain", SetSize = new(500, 300) }, false);
            fancy = Open(new FancyWindow { Title = "Fancy", SetSize = new(500, 300) }, true);
            @default = Open(new DefaultWindow { Title = "Default", SetSize = new(500, 300) }, true);
            narrow = Open(new FancyWindow { Title = "A rather long window title", SetSize = new(220, 150) }, true);
            radar = new RadarConsoleWindow();
            radar.OpenCentered();
        });
        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            foreach (var window in new BaseWindow[] { fancy!, @default!, radar! })
            {
                var badge = Find(window, WFWindowBranding.BadgeName);
                var rule = Find(window, WFWindowBranding.RuleName);
                Assert.That(badge, Is.Not.Null, $"{window.GetType().Name} has no badge");
                Assert.That(badge!.Visible && badge.Width > 0, $"{window.GetType().Name} hides its badge");
                Assert.That(rule, Is.Not.Null, $"{window.GetType().Name} has no rule");
                Assert.That(rule!.Width, Is.GreaterThan(window.Width * 0.9f), "the rule doesn't span the window");
                Assert.That(rule.Height, Is.EqualTo(2f).Within(0.01f));
            }

            // Branding keeps the header height and replaces the plain divider.
            var plainRow = Find(plain!, "WindowTitle")!.Parent!;
            var fancyRow = Find(fancy!, "WindowTitle")!.Parent!;
            Assert.That(fancyRow.Height, Is.EqualTo(plainRow.Height).Within(0.5f), "the badge made the header taller");
            Assert.That(Descendants(fancy!).OfType<PanelContainer>().Any(p => p.HasStyleClass("LowDivider") && p.Visible),
                Is.False, "the plain divider still shows under the rule");

            Assert.That(Find(narrow!, WFWindowBranding.BadgeName)!.Visible, Is.False,
                "the badge crowds a narrow window's title");

            // A second call adds nothing.
            WFWindowBranding.Apply(fancy!);
            Assert.That(Descendants(fancy!).Count(c => c.Name == WFWindowBranding.RuleName), Is.EqualTo(1));

            foreach (var window in new BaseWindow[] { plain!, fancy!, @default!, narrow!, radar! })
                window.Close();
        });

        await pair.CleanReturnAsync();
    }

    private static T Open<T>(T window, bool brand) where T : BaseWindow
    {
        if (brand)
            WFWindowBranding.Apply(window);
        window.OpenCentered();
        return window;
    }

    private static Control? Find(Control root, string name)
    {
        return Descendants(root).FirstOrDefault(c => c.Name == name);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
