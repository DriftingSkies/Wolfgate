#nullable enable
using System.Reflection;
using Content.Client._WF.Options.UI.Tabs;
using Content.Client.Options.UI;
using Content.Shared._WF.CCVar;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._WF.Options;

/// <summary>The Extra tab of the options window holds the Wolfgate settings and applies them.</summary>
[TestFixture]
[TestOf(typeof(WolfgateExtraTab))]
public sealed class WolfgateExtraTabTest
{
    [Test]
    public async Task ExtraTabAppliesSettings()
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
                var tab = menu.FindControl<WolfgateExtraTab>("ExtraTab");
                Assert.That(menu.Tabs.GetChild(6), Is.SameAs(tab));
                var haze = tab.FindControl<CheckBox>("HeatHazeCheckBox");
                var apply = tab.Control.FindControl<Button>("ApplyButton");
                Assert.That(haze.Pressed, Is.EqualTo(original));
                Assert.That(apply.Disabled, Is.True);

                haze.Pressed = !original;
                tab.Control.ValueChanged();
                Assert.That(apply.Disabled, Is.False, "A changed setting must offer Apply.");
                // Buttons only raise OnPressed from input, so call the row's own handler.
                typeof(OptionsTabControlRow).GetMethod("ApplyButtonPressed", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(tab.Control, new object?[] { null });
            }
            finally
            {
                var applied = config.GetCVar(HeatHazeCVars.Enabled);
                config.SetCVar(HeatHazeCVars.Enabled, original);
                menu.Dispose();
                Assert.That(applied, Is.EqualTo(!original), "Apply must write the setting.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
