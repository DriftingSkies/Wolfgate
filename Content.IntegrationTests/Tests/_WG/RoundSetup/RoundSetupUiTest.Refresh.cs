using System.Linq;
using Content.Client._WG.Loadouts;
using Content.Client.Lobby;
using Content.Server.Preferences.Managers;
using Content.Shared.Preferences;
using System.Numerics;
using Content.Client._WG.RoundSetup;
using Content.Shared._WG.RoundSetup;
using Robust.Shared.Localization;
using Robust.Client.Graphics;
using Content.Client.Lobby.UI;
using Robust.Shared;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests._WG.RoundSetup;

public sealed partial class RoundSetupUiTest
{
    /// <summary>Live crew rows reserve enough space for the button, icon and full font height.</summary>
    [TestCase(1f)]
    [TestCase(1.25f)]
    [TestCase(1.5f)]
    public async Task ActiveCrewRowsFitAtUiScale(float scale)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var client = pair.Client;
        var ui = client.ResolveDependency<IUserInterfaceManager>();
        var previousScale = client.CfgMan.GetCVar(CVars.DisplayUIScale);
        var previousAutoScale = client.CfgMan.GetCVar(CVars.ResAutoScaleEnabled);
        var id = 0;
        try
        {
            await client.WaitPost(() =>
            {
                client.CfgMan.SetCVar(CVars.ResAutoScaleEnabled, false);
                client.CfgMan.SetCVar(CVars.DisplayUIScale, scale);
            });
            await pair.RunTicksSync(5);
            var state = await Request(pair, new(WGRoundSetupAction.Create) { Scenario = WGRoundScenario.Ship, Title = "AES Colossus" });
            id = state.OwnLobby!.Value;
            await client.WaitAssertion(() =>
            {
                var screen = (LobbyGui) ui.ActiveScreen!;
                var panel = Named<WGRoundSetupLobbyPanel>(screen, "ActiveCrews");
                var row = Descendants(panel).OfType<WGRoundSetupCrewRow>().Single();
                Assert.That(row.UIScale, Is.EqualTo(scale).Within(0.01f));
                for (var parent = row.Parent; parent != null && parent != panel.Parent; parent = parent.Parent)
                {
                    Assert.That(row.GlobalPosition.Y, Is.GreaterThanOrEqualTo(parent.GlobalPosition.Y - 1), "The full row must be visible inside the crew list.");
                    Assert.That(row.GlobalPosition.Y + row.Height, Is.LessThanOrEqualTo(parent.GlobalPosition.Y + parent.Height + 1), "The full row must be visible inside the crew list.");
                }
                foreach (var control in Descendants(row).Where(c => c.VisibleInTree && c is Label or TextureRect))
                {
                    var bottom = control.GlobalPosition.Y + control.Height;
                    Assert.That(bottom, Is.LessThanOrEqualTo(row.GlobalPosition.Y + row.Height + 1), "The row must contain its text and icon.");
                    if (control is Label label)
                    {
                        var font = label.FontOverride ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled) ? styled : ui.ThemeDefaults.DefaultFont);
                        Assert.That(label.PixelHeight + 1, Is.GreaterThanOrEqualTo(font.GetHeight(label.UIScale)), label.Text);
                    }
                }
            });
        }
        finally
        {
            if (id != 0) await Request(pair, new(WGRoundSetupAction.Leave) { LobbyId = id });
            await client.WaitPost(() =>
            {
                client.CfgMan.SetCVar(CVars.DisplayUIScale, previousScale);
                client.CfgMan.SetCVar(CVars.ResAutoScaleEnabled, previousAutoScale);
            });
            await pair.RunTicksSync(3);
        }
        await pair.CleanReturnAsync();
    }

    /// <summary>Live crew changes cannot destroy an active text field or an open dropdown.</summary>
    [Test]
    public async Task BackgroundUpdatesPreserveTypingAndDropdowns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new(WGRoundSetupAction.Create) { Scenario = WGRoundScenario.Ship });
        var id = state.OwnLobby!.Value;
        WGRoundSetupWindow window = null!;
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        await pair.Client.WaitPost(() =>
        {
            window = new WGRoundSetupWindow();
            window.FitTo(new Vector2(1120, 780));
            window.OpenCentered();
        });
        await pair.RunTicksSync(3);
        Button admission = null!;
        await pair.Client.WaitPost(() => admission = Buttons(Named<Control>(window, "RoundSetupSections"), "admission").Single());
        await Click(pair, admission);
        LineEdit title = null!;
        await pair.Client.WaitPost(() =>
        {
            title = Descendants(window).OfType<LineEdit>().Single(edit => edit.PlaceHolder == Loc.GetString("wg-round-setup-crew-title-placeholder"));
            title.GrabKeyboardFocus();
        });
        await Request(pair, new(WGRoundSetupAction.Configure) { LobbyId = id, Title = "Live title", Access = WGLobbyAccess.Public });
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(title.IsInsideTree, Is.True);
            Assert.That(ui.KeyboardFocused, Is.SameAs(title));
            title.ReleaseKeyboardFocus();
        });
        await pair.RunTicksSync(3);
        WGRoundSetupOptionButton selector = null!;
        await pair.Client.WaitPost(() => selector = Descendants(window).OfType<WGRoundSetupOptionButton>().Single(button => !button.Disabled));
        await Click(pair, selector);
        await Request(pair, new(WGRoundSetupAction.Configure) { LobbyId = id, Title = "Another title", Access = WGLobbyAccess.Public });
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(selector.IsInsideTree, Is.True);
            Assert.That(selector.OptionsScroll.VisibleInTree, Is.True);
            window.Close();
        });
        await Request(pair, new(WGRoundSetupAction.Leave) { LobbyId = id });
        await pair.CleanReturnAsync();
    }

    /// <summary>The chosen crew job opens its normal picker and saves the selected kit to that character slot.</summary>
    [Test]
    public async Task CrewLoadoutPickerSavesTheSelectedJobsEquipment()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var prefs = pair.Server.ResolveDependency<IServerPreferencesManager>();
        var slot = prefs.GetPreferences(pair.Player!.UserId).SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) prefs.GetPreferences(pair.Player.UserId).SelectedCharacter!;
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientPreferencesManager>().UpdateCharacter(
            HumanoidCharacterProfile.DefaultWithSpecies("Human").WithName("Loadout Tester"), slot));
        await pair.RunTicksSync(pair.SecondsToTicks(1));
        var state = await Request(pair, new(WGRoundSetupAction.Create) { Scenario = WGRoundScenario.Ship });
        var id = state.OwnLobby!.Value;
        await Request(pair, new(WGRoundSetupAction.SelectJob) { LobbyId = id, Job = "Contractor" });
        WGRoundSetupWindow window = null!;
        Button button = null!;
        WolfgateLoadoutWindow editor = null!;
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        await pair.Client.WaitPost(() =>
        {
            window = new WGRoundSetupWindow();
            window.FitTo(new Vector2(1120, 780));
            window.OpenCentered();
        });
        await pair.RunTicksSync(3);
        await pair.Client.WaitAssertion(() =>
        {
            button = Buttons(window, "job-loadout").Single();
            Assert.That(Labels(window).Any(text => text.Contains("Loadout:") && text.Contains("Loadout Tester")), Is.True);
        });
        await Click(pair, button);
        await pair.Client.WaitPost(() =>
        {
            editor = Descendants(ui.WindowRoot).OfType<WolfgateLoadoutWindow>().Single();
            editor.OpenGroup(editor.Catalog.Groups.Single(group => group.Id == "MercenaryJumpsuit"));
        });
        await pair.RunTicksSync(4);
        LoadoutTile tile = null!;
        await pair.Client.WaitAssertion(() => tile = Descendants(editor).OfType<LoadoutTile>()
            .Single(choice => choice.VisibleInTree && choice.Entry?.Id == "MercenaryClothingUniformJumpskirtMercenary"));
        await Click(pair, tile);
        await pair.Client.WaitPost(() => editor.Close());
        await pair.RunTicksSync(pair.SecondsToTicks(1));
        await pair.Server.WaitAssertion(() =>
        {
            var profile = (HumanoidCharacterProfile) prefs.GetPreferences(pair.Player.UserId).Characters[slot];
            Assert.That(profile.Loadouts["JobContractor"].SelectedLoadouts["MercenaryJumpsuit"].Single().Prototype.Id,
                Is.EqualTo("MercenaryClothingUniformJumpskirtMercenary"));
        });
        await pair.Client.WaitAssertion(() => { Assert.That(window.VisibleInTree, Is.True); window.Close(); });
        await Request(pair, new(WGRoundSetupAction.Leave) { LobbyId = id });
        await pair.Server.WaitPost(() => prefs.SetProfile(pair.Player.UserId, slot, original).Wait());
        await pair.CleanReturnAsync();
    }
}
