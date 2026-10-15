using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.RoundSetup;
using Content.Client._WF.ShipPreview;
using Content.Client.Lobby.UI;
using Content.Client._NF.LateJoin.Windows;
using Content.Client._NF.LateJoin.Controls;
using Content.Server.GameTicking;
using Content.Client.GameTicking.Managers;
using Content.IntegrationTests.Pair;
using Content.Shared._NF.Bank;
using Content.Shared._WF.RoundSetup;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>Checks live lobby discovery, deliberate scenario creation and usable setup-window geometry.</summary>
[TestFixture]
[TestOf(typeof(WFRoundSetupWindow))]
[TestOf(typeof(WFRoundSetupLobbyPanel))]
public sealed class RoundSetupUiTest
{
    /// <summary>The main screen follows live membership and opens details without joining another lobby.</summary>
    [Test]
    public async Task MainLobbyPanelListsLiveGroupsAndOpensTheirDetails()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        WFRoundSetupLobbyPanel panel = null!;
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(ui.ActiveScreen, Is.TypeOf<LobbyGui>());
            panel = Descendants(ui.ActiveScreen!).OfType<WFRoundSetupLobbyPanel>().Single();
            Assert.That(panel.VisibleInTree, Is.True);
            Assert.That(Labels(panel), Does.Contain(Loc.GetString("wf-round-setup-panel-empty")));
        });

        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Normal });
        Assert.That(state.OwnLobby, Is.Not.Null);
        var lobby = state.Lobbies.Single(entry => entry.Id == state.OwnLobby);
        Button card = null!;
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Labels(panel), Does.Contain(Loc.GetString("wf-round-setup-panel-host", ("host", lobby.HostName))));
            Assert.That(Labels(panel), Does.Contain(Loc.GetString("wf-round-setup-scenario-normal")));
            card = Descendants(panel).OfType<Button>().Single(button => button.ToolTip ==
                Loc.GetString("wf-round-setup-panel-open-help", ("host", lobby.HostName)));
            var screen = (LobbyGui) ui.ActiveScreen!;
            screen.Measure(new Vector2(1280, 720));
            screen.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(1280, 720)));
            var leave = screen.LeaveButton;
            Assert.That(panel.GlobalPosition.X, Is.GreaterThan(leave.GlobalPosition.X + leave.Size.X),
                "Crews have their own column beside the menu.");
            Assert.That(panel.Height, Is.GreaterThanOrEqualTo(180));
            Assert.That(panel.GlobalPosition.Y + panel.Height, Is.LessThanOrEqualTo(720), "Lobby list must remain on-screen at 720p.");
            var scroll = Descendants(panel).OfType<ScrollContainer>().Single();
            Assert.That(scroll.VScrollEnabled, Is.True);
            Assert.That(scroll.HScrollEnabled, Is.False);
        });
        await Click(pair, card);
        await pair.Client.WaitAssertion(() =>
        {
            var window = Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>().Single();
            Assert.That(window.IsOpen, Is.True);
            Assert.That(Labels(window), Does.Contain(Loc.GetString("wf-round-setup-your-lobby",
                ("scenario", Loc.GetString("wf-round-setup-scenario-normal")))));
            window.Close();
            panel.Visible = false;
        });
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = lobby.Id });
        Assert.That(state.OwnLobby, Is.Null);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Labels(panel), Does.Contain(Loc.GetString("wf-round-setup-panel-host", ("host", lobby.HostName))),
                "Hidden panels should release their live snapshot callback.");
            panel.Visible = true;
            Assert.That(Labels(panel), Does.Contain(Loc.GetString("wf-round-setup-panel-empty")),
                "Returning to the panel must immediately use the latest snapshot.");
            Assert.That(Descendants(panel).OfType<Button>().Any(button => button.ToolTip == card.ToolTip), Is.False);
        });
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }

    /// <summary>Opening Join is inert; preparation is first and fixed actions remain accessible at every size.</summary>
    [TestCase(800, 600)]
    [TestCase(1280, 720)]
    [TestCase(1920, 1080)]
    public async Task SetupFlowFitsViewportAndBrowsingDoesNotCreate(int width, int height)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(state.OwnLobby, Is.Null);
        var viewport = new Vector2(width, height);
        WFRoundSetupWindow window = null!;
        Button button = null!;
        await pair.Client.WaitPost(() =>
        {
            window = new WFRoundSetupWindow();
            window.FitTo(viewport);
            window.OpenCentered();
            window.ShowJoin();
        });
        await pair.RunTicksSync(2);
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, viewport);
            Assert.That(window.Size.X, Is.EqualTo(Math.Min(1120, width - 32)));
            Assert.That(pair.Client.System<ClientGameTicker>().AreWeReady, Is.False);
            Assert.That(Named<Button>(window, "RoundSetupJoinNormally").Disabled, Is.False);
            button = Named<Button>(window, "RoundSetupJoinLobby");
        });
        await Click(pair, button);
        await pair.Client.WaitPost(() => button = Buttons(window, "flow-create").Single());
        await Click(pair, button);
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, viewport);
            Assert.That(Labels(window), Does.Contain(Loc.GetString("wf-round-setup-choose-scenario")));
            Assert.That(Descendants(window).OfType<Button>().Count(control => control.Name?.StartsWith("RoundSetupCreate") == true), Is.EqualTo(3));
            Assert.That(pair.Client.System<WFRoundSetupClientSystem>().State!.OwnLobby, Is.Null);
            button = Named<Button>(window, "RoundSetupCreateNormal");
        });
        await Click(pair, button);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        var lobbyId = state.OwnLobby!.Value;
        var lobby = state.Lobbies.Single(entry => entry.Id == lobbyId);
        Assert.That(lobby.Roles, Is.Empty);
        Assert.That(lobby.Members.Length, Is.EqualTo(1));
        await pair.Client.WaitPost(() => window.ShowLobby(lobbyId));
        await pair.RunTicksSync(2);
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, viewport);
            Assert.That(Descendants(window).OfType<OptionButton>().Any(control => control.Name == "RoundSetupPersonalJob"), Is.True);
            Assert.That(Buttons(window, "select-role"), Is.Empty);
            Assert.That(Named<CheckBox>(window, "RoundSetupReady").Disabled, Is.True);
            Assert.That(Descendants(window).Any(control => control.Name == "RoundSetupNext"), Is.False);
        });
        OptionButton jobPicker = null!;
        await pair.Client.WaitAssertion(() =>
        {
            jobPicker = Named<OptionButton>(window, "RoundSetupPersonalJob");
            Assert.That(jobPicker.IsItemDisabled(0), Is.True, "The placeholder cannot replace a committed job visually.");
        });
        await Click(pair, jobPicker);
        await pair.Client.WaitPost(() => button = Descendants(jobPicker.OptionsScroll).OfType<Button>().First(option =>
            option.Text != Loc.GetString("wf-round-setup-job-unselected")));
        await Click(pair, button);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(state.Lobbies.Single(entry => entry.Id == lobbyId).Members.Single().Job, Is.Not.Null);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Named<CheckBox>(window, "RoundSetupReady").Disabled, Is.False);
            button = Buttons(Named<Control>(window, "RoundSetupSections"), "crew-section").Single();
        });
        await Click(pair, button);
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, viewport);
            Assert.That(Labels(window), Does.Contain(Loc.GetString("wf-round-setup-roles-empty-host")));
            Assert.That(Descendants(window).OfType<SpinBox>(), Is.Empty, "An empty crew must not show a prefilled role.");
            button = Buttons(window, "role-new").Single();
        });
        await Click(pair, button);
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, viewport);
            Assert.That(Descendants(window).OfType<SpinBox>().Single().Value, Is.EqualTo(1));
            button = Buttons(window, "role-cancel").Single();
        });
        await Click(pair, button);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Descendants(window).OfType<SpinBox>(), Is.Empty);
            button = Buttons(Named<Control>(window, "RoundSetupSections"), "admission").Single();
        });
        await Click(pair, button);
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, viewport);
            Assert.That(Buttons(window, "save-settings").Count(), Is.EqualTo(1));
            window.Close();
        });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = lobbyId });
        await pair.CleanReturnAsync();
    }

    /// <summary>The main Join action opens choices; only Join Normally changes pregame readiness.</summary>
    [Test]
    public async Task JoinNormallyReadiesWithoutCreatingLobby()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Button join = null!;
        await pair.Client.WaitAssertion(() =>
        {
            var lobby = (LobbyGui) ui.ActiveScreen!;
            Assert.That(lobby.ReadyButton.ToggleMode, Is.False);
            Assert.That(lobby.RoundSetupButton.VisibleInTree, Is.True);
            join = lobby.ReadyButton;
        });
        await Click(pair, join);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(pair.Client.System<ClientGameTicker>().AreWeReady, Is.False);
            var window = Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>().Single();
            join = Named<Button>(window, "RoundSetupJoinNormally");
        });
        await Click(pair, join);
        await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(pair.Client.System<ClientGameTicker>().AreWeReady, Is.True);
            Assert.That(pair.Client.System<WFRoundSetupClientSystem>().State!.OwnLobby, Is.Null);
            Assert.That(Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>(), Is.Empty);
            pair.Client.ResolveDependency<Robust.Shared.Console.IConsoleHost>().ExecuteCommand("toggleready false");
        });
        await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
        await pair.CleanReturnAsync();
    }

    /// <summary>Late join hides the extra setup button and opens the ordinary station jobs directly.</summary>
    [Test]
    public async Task MidroundJoinOpensChoicesThenNormalJobs()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            ticker.StartRound(force: true);
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            Assert.That(pair.Player!.AttachedEntity, Is.Null);
        });
        await pair.RunTicksSync(2);
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Button join = null!;
        await pair.Client.WaitAssertion(() =>
        {
            var lobby = (LobbyGui) ui.ActiveScreen!;
            Assert.That(lobby.RoundSetupButton.Visible, Is.False);
            Assert.That(lobby.ReadyButton.ToggleMode, Is.False);
            join = lobby.ReadyButton;
        });
        await Click(pair, join);
        await pair.Client.WaitPost(() =>
        {
            var window = Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>().Single();
            join = Named<Button>(window, "RoundSetupJoinNormally");
        });
        await Click(pair, join);
        await pair.Client.WaitAssertion(() =>
        {
            var picker = Descendants(ui.WindowRoot).OfType<PickerWindow>().Single();
            Assert.That(Descendants(picker).OfType<StationPickerControl>().Count(), Is.EqualTo(1));
            Assert.That(Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>(), Is.Empty);
            Assert.That(pair.Client.System<WFRoundSetupClientSystem>().State!.OwnLobby, Is.Null);
            picker.Close();
        });
        await pair.Server.WaitPost(() => pair.Server.System<GameTicker>().RestartRound());
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }

    /// <summary>Ship browsing loads the real preview without selecting or buying, and releases its map when leaving.</summary>
    [Test]
    public async Task ShipPreviewLoadsBeforeSelectionAndReleasesAfterNavigation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
        var lobbyId = state.OwnLobby!.Value;
        var vessel = state.Vessels.First();
        var balance = state.HostBalance;
        var maps = pair.Client.System<SharedMapSystem>();
        HashSet<MapId> originalMaps = null!;
        WFRoundSetupWindow window = null!;
        Button shipPage = null!;
        await pair.Client.WaitPost(() =>
        {
            originalMaps = maps.GetAllMapIds().ToHashSet();
            window = new WFRoundSetupWindow();
            window.FitTo(new Vector2(1280, 720));
            window.OpenCentered();
            window.ShowLobby(lobbyId);
        });
        await pair.RunTicksSync(2);
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, new Vector2(1280, 720));
            shipPage = Buttons(Named<Control>(window, "RoundSetupSections"), "ship").Single();
        });
        await Click(pair, shipPage);
        ShipPreviewControl preview = null!;
        OptionButton selector = null!;
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, new Vector2(1280, 720));
            preview = Named<ShipPreviewControl>(window, "RoundSetupShipPreview");
            selector = Named<OptionButton>(window, "RoundSetupShipSelector");
            Assert.That(preview.HasPreview, Is.False, "Opening ship options must not choose a vessel implicitly.");
        });
        await Click(pair, selector);
        Button option = null!;
        await pair.Client.WaitAssertion(() =>
        {
            var caption = Loc.GetString("wf-round-setup-ship-option", ("ship", vessel.Name),
                ("price", BankSystemExtensions.ToSpesoString(vessel.Price)));
            // Multiple vessel prototypes may share a name and price; the first match follows catalog order.
            option = Descendants(selector.OptionsScroll).OfType<Button>().First(button => button.Text == caption);
        });
        await Click(pair, option);
        await PoolManager.WaitUntil(pair.Client, () => preview.HasPreview, maxTicks: 30);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(preview.TileCount, Is.Positive, "The ship preview must load actual grid tiles.");
            Assert.That(preview.GridSize.X, Is.Positive);
            Assert.That(preview.GridSize.Y, Is.Positive);
            Assert.That(maps.GetAllMapIds().Except(originalMaps).Count(), Is.EqualTo(1));
        });
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(state.Lobbies.Single(lobby => lobby.Id == lobbyId).Vessel, Is.Null,
            "Previewing a ship must leave the server's selection unchanged.");
        Assert.That(state.HostBalance, Is.EqualTo(balance));
        Button select = null!;
        await pair.Client.WaitPost(() => select = Named<Button>(window, "RoundSetupSelectShip"));
        await Click(pair, select);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(state.Lobbies.Single(lobby => lobby.Id == lobbyId).Vessel, Is.EqualTo(vessel.Id));
        Assert.That(state.HostBalance, Is.EqualTo(balance), "Selecting the previewed ship must not charge before launch.");

        Button overview = null!;
        await pair.Client.WaitPost(() => overview = Buttons(Named<Control>(window, "RoundSetupSections"), "flow-overview").Single());
        await Click(pair, overview);
        await pair.RunTicksSync(2);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(preview.HasPreview, Is.False);
            Assert.That(maps.GetAllMapIds(), Is.EquivalentTo(originalMaps), "Leaving ship options must release its private preview map.");
            shipPage = Buttons(Named<Control>(window, "RoundSetupSections"), "ship").Single();
        });
        await Click(pair, shipPage);
        await pair.Client.WaitPost(() => preview = Named<ShipPreviewControl>(window, "RoundSetupShipPreview"));
        await PoolManager.WaitUntil(pair.Client, () => preview.HasPreview, maxTicks: 30);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(preview.TileCount, Is.Positive, "Returning to ship options must preview the committed vessel.");
            window.Close();
            Assert.That(preview.HasPreview, Is.False);
            Assert.That(maps.GetAllMapIds(), Is.EquivalentTo(originalMaps), "Closing setup must release the preview map immediately.");
        });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = lobbyId });
        await pair.CleanReturnAsync();
    }

    /// <summary>Long selected values and popup choices wrap instead of widening or clipping the control.</summary>
    [Test]
    public async Task LongSelectorTextRemainsReadable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        await pair.Client.WaitAssertion(() =>
        {
            const string longText = "Expedition medical officer aboard a very long named civilian research station";
            var selector = new WFRoundSetupOptionButton();
            pair.Client.ResolveDependency<IUserInterfaceManager>().RootControl.AddChild(selector);
            selector.AddItem(longText, 1);
            selector.SelectId(1);
            selector.Measure(new Vector2(240, 500));
            selector.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(240, selector.DesiredSize.Y)));
            Assert.That(selector.Width, Is.EqualTo(240));
            var caption = Descendants(selector).OfType<RichTextLabel>().Single();
            Assert.That(caption.GetFormattedMessage()!.ToString(), Is.EqualTo(longText));
            Assert.That(caption.Height, Is.GreaterThan(25), "Long selected text must wrap onto more than one line.");
            Assert.That(caption.Size.X, Is.LessThanOrEqualTo(selector.Width));
            selector.OptionsScroll.Measure(new Vector2(240, 500));
            selector.OptionsScroll.Arrange(UIBox2.FromDimensions(Vector2.Zero, selector.OptionsScroll.DesiredSize));
            var option = Descendants(selector.OptionsScroll).OfType<Button>().Single();
            Assert.That(option.Label.Visible, Is.False);
            Assert.That(Descendants(option).OfType<RichTextLabel>().Single().Height, Is.GreaterThan(25));
            selector.Orphan();
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The countdown's live readout fits its compact banner and never intercepts game input.</summary>
    [Test]
    public async Task CrashCountdownBannerFitsItsReadout()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        await pair.Client.WaitAssertion(() =>
        {
            var hud = new WFCrashCountdownHud(pair.Client.ResolveDependency<IResourceCache>());
            pair.Client.ResolveDependency<IUserInterfaceManager>().RootControl.AddChild(hud);
            foreach (var (remaining, clock) in new[] { (180, "03:00"), (29, "00:29") })
            {
                hud.SetRemaining(remaining);
                hud.Measure(new Vector2(WFCrashCountdownHud.PanelWidth, 120));
                hud.Arrange(UIBox2.FromDimensions(Vector2.Zero, hud.DesiredSize));
                Assert.That(Labels(hud), Does.Contain(clock));
                Assert.That(hud.Size.X, Is.LessThanOrEqualTo(WFCrashCountdownHud.PanelWidth));
                Assert.That(hud.Size.Y, Is.LessThanOrEqualTo(120));
                foreach (var control in Descendants(hud).Prepend(hud))
                {
                    Assert.That(control.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore));
                    var position = control.GlobalPosition - hud.GlobalPosition;
                    var end = position + control.Size;
                    var description = control is Label label ? label.Text : control.GetType().Name;
                    Assert.That(position.X, Is.GreaterThanOrEqualTo(-1), description);
                    Assert.That(position.Y, Is.GreaterThanOrEqualTo(-1), description);
                    Assert.That(end.X, Is.LessThanOrEqualTo(hud.Size.X + 1), description);
                    Assert.That(end.Y, Is.LessThanOrEqualTo(hud.Size.Y + 1), description);
                }
            }
            hud.Orphan();
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Measures real controls and rejects horizontal overflow or inaccessible fixed navigation and footer actions.</summary>
    private static void AssertFits(WFRoundSetupWindow window, Vector2 viewport)
    {
        for (var i = 0; i < 2; i++)
        {
            window.Measure(viewport);
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, window.DesiredSize));
        }
        var size = window.Size;
        var page = Named<Control>(window, "RoundSetupPage");
        var scroll = Named<ScrollContainer>(window, "RoundSetupPageScroll");
        Assert.That(scroll.HScrollEnabled, Is.False);
        Assert.That(scroll.Height, Is.GreaterThan(100), "Leave usable space for the scrolling page.");
        Assert.That(page.Width, Is.LessThanOrEqualTo(scroll.Width + 1));
        Assert.That(size.X, Is.LessThanOrEqualTo(viewport.X - 32 + 1));
        Assert.That(size.Y, Is.LessThanOrEqualTo(viewport.Y - 32 + 1));
        foreach (var button in Descendants(window).Where(control => control.VisibleInTree &&
                     (control is BaseButton || control is RichTextLabel)))
        {
            var position = button.GlobalPosition - window.GlobalPosition;
            var end = position + button.Size;
            var description = button is Button text ? text.Text : button.Name;
            Assert.That(position.X, Is.GreaterThanOrEqualTo(-1), description);
            Assert.That(end.X, Is.LessThanOrEqualTo(size.X + 1), description);
            if (HasScrollingParent(button, window))
                continue;
            Assert.That(position.Y, Is.GreaterThanOrEqualTo(-1), description);
            Assert.That(end.Y, Is.LessThanOrEqualTo(size.Y + 1), description);
        }
    }

    private static bool HasScrollingParent(Control control, Control root)
    {
        for (var parent = control.Parent; parent != null && parent != root; parent = parent.Parent)
        {
            if (parent is ScrollContainer)
                return true;
        }
        return false;
    }

    /// <summary>Finds dynamically constructed controls that are not registered in a XAML name scope.</summary>
    private static T Named<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().Single(control => control.Name == name);

    private static IEnumerable<string> Labels(Control control) => Descendants(control).OfType<Label>().Select(label => label.Text)
        .Concat(Descendants(control).OfType<RichTextLabel>().Select(label => label.GetFormattedMessage()?.ToString() ?? string.Empty));

    private static IEnumerable<Button> Buttons(Control control, string key) => Descendants(control).OfType<Button>()
        .Where(button => button.VisibleInTree && button.Text == Loc.GetString($"wf-round-setup-{key}"));

    /// <summary>Clicks a live button at its current position, including when the root layout changes between ticks.</summary>
    private static async Task Click(TestPair pair, BaseButton button)
    {
        var pressed = false;
        void OnPressed(BaseButton.ButtonEventArgs _) => pressed = true;
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(button.VisibleInTree, Is.True, button.Name);
            Assert.That(button.Disabled, Is.False, button.Name);
            Assert.That(button.Size.X, Is.Positive, button.Name);
            Assert.That(button.Size.Y, Is.Positive, button.Name);
            button.OnPressed += OnPressed;
        });
        try
        {
            foreach (var state in new[] { BoundKeyState.Down, BoundKeyState.Up })
            {
                GUIBoundKeyEventArgs args = null!;
                await pair.Client.WaitPost(() =>
                {
                    var pointer = new ScreenCoordinates(button.GlobalPixelPosition + button.PixelSize / 2, button.Window?.Id ?? default);
                    var relative = pointer.Position / button.UIScale - button.GlobalPosition;
                    var pixels = pointer.Position - button.GlobalPixelPosition;
                    args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, pointer, default, relative, pixels);
                });
                await pair.Client.DoGuiEvent(button, args);
                await pair.RunTicksSync(1);
            }
            await pair.Client.WaitAssertion(() => Assert.That(pressed, Is.True, "The real button event must fire before checking its resulting view."));
        }
        finally
        {
            await pair.Client.WaitPost(() => button.OnPressed -= OnPressed);
        }
    }

    /// <summary>Sends real serialized setup actions with enough simulated time for the server's rate limit.</summary>
    private static async Task<WFRoundSetupState> Request(TestPair pair, WFRoundSetupRequest request)
    {
        await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
        await pair.Client.WaitPost(() => pair.Client.System<WFRoundSetupClientSystem>().Send(request));
        await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
        WFRoundSetupState state = null!;
        await pair.Client.WaitAssertion(() =>
        {
            state = pair.Client.System<WFRoundSetupClientSystem>().State!;
            Assert.That(state, Is.Not.Null);
        });
        return state;
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
