#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.IntegrationTests.Pair;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Content.Client._WG.Options.UI;
using Content.Client._WG.Options.UI.Tabs;
using Content.Client.Options.UI;
using Content.Client.Options.UI.Tabs;
using Content.IntegrationTests.Tests._WG.EngineCompat;
using Content.Shared._WG.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WG.Options;

/// <summary>The Wolfgate master tab of the options window holds the fork settings and applies them.</summary>
[TestFixture]
[TestOf(typeof(WolfgateOptionsTabs))]
public sealed class WolfgateOptionsTabsTest
{
    [Test]
    public async Task WolfgateTabAppliesSettings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var config = client.ResolveDependency<IConfigurationManager>();

        await client.WaitAssertion(() =>
        {
            var original = config.GetCVar(HeatHazeCVars.Enabled);
            var menu = new OptionsMenu();
            try
            {
                var master = menu.FindControl<TabContainer>("WgMaster");
                var wolfgate = menu.FindControl<WolfgateOptionsTabs>("WolfgateTabs");
                Assert.Multiple(() =>
                {
                    // The window opens on the SS14 side, with only that page shown.
                    Assert.That(master.CurrentTab, Is.EqualTo(OptionsMenu.WgStockPage));
                    Assert.That(menu.Tabs.Visible, Is.True);
                    Assert.That(wolfgate.Visible, Is.False);
                    Assert.That(master.GetChild(OptionsMenu.WgStockPage), Is.SameAs(menu.Tabs));
                    Assert.That(master.GetChild(OptionsMenu.WgWolfgatePage), Is.SameAs(wolfgate));
                    Assert.That(wolfgate.GetChild(OptionsMenu.ConsentTabIndex), Is.InstanceOf<WolfgateConsentTab>());
                    Assert.That(wolfgate.GetChild(WolfgateOptionsTabs.ControlsTabIndex), Is.SameAs(wolfgate.ControlsTab));
                });

                var tab = OptionsTabTestHelper.Find<WGGraphicsTab>(menu);
                var haze = tab.FindControl<CheckBox>("HeatHazeCheckBox");
                var apply = tab.Control.FindControl<Button>("ApplyButton");
                Assert.That(haze.Pressed, Is.EqualTo(original));
                Assert.That(apply.Disabled, Is.True);

                haze.Pressed = !original;
                tab.Control.ValueChanged();
                Assert.That(apply.Disabled, Is.False, "A changed setting must offer Apply.");
                OptionsTabTestHelper.Apply(tab.Control);
            }
            finally
            {
                var applied = config.GetCVar(HeatHazeCVars.Enabled);
                config.SetCVar(HeatHazeCVars.Enabled, original);
                menu.Teardown();
                Assert.That(applied, Is.EqualTo(!original), "Apply must write the setting.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The open window starts on the SS14 side, and clicking the master buttons switches sides.</summary>
    [Test]
    public async Task MasterStripSwitchesSides()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var ui = client.ResolveDependency<IUserInterfaceManager>();
        await pair.RunTicksSync(20);

        OptionsMenu menu = default!;
        TabContainer master = default!;
        List<Button> buttons = default!;
        await client.WaitPost(() =>
        {
            ui.GetUIController<OptionsUIController>().OpenWindow();
            menu = ui.WindowRoot.Children.OfType<OptionsMenu>().Single();
            master = menu.FindControl<TabContainer>("WgMaster");
            buttons = menu.FindControl<WGOptionsMasterStrip>("WgMasterStrip").GetChild(0).Children.OfType<Button>().ToList();
        });
        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            Assert.That(master.CurrentTab, Is.EqualTo(OptionsMenu.WgStockPage));
            Assert.That(menu.Tabs.Visible, Is.True);
            Assert.That(master.GetChild(OptionsMenu.WgWolfgatePage).Visible, Is.False);
            Assert.That(buttons[OptionsMenu.WgStockPage].Pressed, Is.True);
            Assert.That(buttons[OptionsMenu.WgWolfgatePage].Pressed, Is.False);
        });

        await Click(pair, buttons[OptionsMenu.WgWolfgatePage]);
        await client.WaitAssertion(() =>
        {
            Assert.That(master.CurrentTab, Is.EqualTo(OptionsMenu.WgWolfgatePage));
            Assert.That(menu.Tabs.Visible, Is.False);
            Assert.That(buttons[OptionsMenu.WgWolfgatePage].Pressed, Is.True);
        });

        await Click(pair, buttons[OptionsMenu.WgStockPage]);
        await client.WaitAssertion(() => Assert.That(master.CurrentTab, Is.EqualTo(OptionsMenu.WgStockPage)));

        // The consent shortcut opens the Wolfgate side on its Consent tab.
        await client.WaitPost(() => ui.GetUIController<OptionsUIController>().OpenWindow(OptionsMenu.ConsentTabIndex));
        await client.WaitAssertion(() =>
        {
            Assert.That(master.CurrentTab, Is.EqualTo(OptionsMenu.WgWolfgatePage));
            Assert.That(menu.FindControl<WolfgateOptionsTabs>("WolfgateTabs").CurrentTab, Is.EqualTo(OptionsMenu.ConsentTabIndex));
        });

        await client.WaitPost(() => menu.Close());
        await pair.CleanReturnAsync();
    }

    private static async Task Click(TestPair pair, Control control)
    {
        var screen = new ScreenCoordinates(control.GlobalPixelPosition + control.PixelSize / 2, control.Window?.Id ?? default);
        var rel = screen.Position / control.UIScale - control.GlobalPosition;
        var relPix = screen.Position - control.GlobalPixelPosition;

        foreach (var state in new[] { BoundKeyState.Down, BoundKeyState.Up })
        {
            await pair.Client.DoGuiEvent(control, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, screen, default, rel, relPix));
            await pair.RunTicksSync(1);
        }
    }

    /// <summary>Stock binds stay on the SS14 Controls tab, fork binds move to the Wolfgate one, with their headers.</summary>
    [Test]
    public async Task KeybindsSplitBetweenSides()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var loc = client.ResolveDependency<ILocalizationManager>();

        await client.WaitAssertion(() =>
        {
            var stock = new KeyRebindTab();
            var fork = new KeyRebindTab(true);

            bool Has(KeyRebindTab tab, string id) =>
                OptionsTabTestHelper.Descendants(tab.FindControl<BoxContainer>("KeybindsContainer"))
                    .OfType<Label>()
                    .Any(l => l.Text == loc.GetString(id));

            Assert.Multiple(() =>
            {
                Assert.That(Has(stock, "ui-options-function-move-up"), Is.True);
                Assert.That(Has(fork, "ui-options-function-move-up"), Is.False);
                Assert.That(Has(stock, "ui-options-function-toggle-standing"), Is.False);
                Assert.That(Has(fork, "ui-options-function-toggle-standing"), Is.True);

                // The targeting section is all fork binds; the map editor one is all stock.
                Assert.That(Has(stock, "ui-options-header-targeting"), Is.False);
                Assert.That(Has(fork, "ui-options-header-targeting"), Is.True);
                Assert.That(Has(fork, "ui-options-header-map-editor"), Is.False);

                Assert.That(stock.FindControl<Button>("Ss13DefaultsButton").Visible, Is.False);
                Assert.That(fork.FindControl<Button>("Ss13DefaultsButton").Visible, Is.True);
            });

            // No section is left as a bare header, and the list doesn't open on a spacer.
            var binds = fork.FindControl<BoxContainer>("KeybindsContainer").Children.ToList();
            Assert.That(binds[0], Is.InstanceOf<Label>());
            for (var i = 0; i < binds.Count; i++)
            {
                if (binds[i] is Label)
                    Assert.That(i + 1 < binds.Count && binds[i + 1] is not Label && binds[i + 1].GetType() != typeof(Control),
                        $"Header {((Label) binds[i]).Text} has no rows.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
