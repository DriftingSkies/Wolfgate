using Content.Client.Changelog;
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
using Content.Shared._WF.CCVar;
using Content.Client.Voting;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.CCVar;
using Content.Client.Voting.UI;
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
public sealed partial class RoundSetupUiTest
{
    [TestCase(0, "players", "slots", "openings")]
    [TestCase(1, "player", "slot", "opening")]
    [TestCase(2, "players", "slots", "openings")]
    public async Task CrewCountsUseSingularAndPlural(int count, string players, string slots, string openings)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Loc.GetString("wf-round-setup-panel-count", ("count", count)), Is.EqualTo($"{count} {players}"));
            Assert.That(Loc.GetString("wf-round-setup-panel-openings", ("count", count)), Is.EqualTo($"{count} open crew {slots}"));
            Assert.That(Loc.GetString("wf-round-setup-panel-members", ("count", count), ("max", WFRoundSetupPolicy.MaximumMembers), ("access", "Public")),
                Is.EqualTo($"{count} / {WFRoundSetupPolicy.MaximumMembers} {players} · Public"));
            Assert.That(Loc.GetString("wf-round-setup-lobby-info", ("count", count), ("access", "Public"), ("status", "Preparing")),
                Is.EqualTo($"{count} {players} · Public · Preparing"));
            Assert.That(Loc.GetString("wf-round-setup-row-tooltip", ("count", count), ("open", count), ("title", "Rescue"), ("host", "Ada"), ("status", "Preparing")),
                Is.EqualTo($"Rescue · Led by Ada · {count} aboard · {count} {openings} · Preparing"));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Crew setup stays out of the main lobby until its button is pressed.</summary>
    [Test]
    public async Task CrewBrowserOpensOnlyWhenRequested()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        await PoolManager.WaitUntil(pair.Client, () => pair.Client.System<WFRoundSetupClientSystem>().State is { CanJoin: true }, maxTicks: 180);
        Button crew = null!;
        await pair.Client.WaitAssertion(() =>
        {
            var screen = (LobbyGui) ui.ActiveScreen!;
            Assert.That(Descendants(screen).OfType<WFRoundSetupLobbyPanel>().Count(), Is.EqualTo(1));
            Assert.That(Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>(), Is.Empty);
            crew = screen.RoundSetupButton;
        });
        await Click(pair, crew);
        await pair.Client.WaitAssertion(() =>
        {
            var window = Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>().Single();
            Assert.That(window.IsOpen, Is.True);
            Assert.That(Labels(window), Does.Contain(Loc.GetString("wf-round-setup-browse")));
            Assert.That(pair.Client.System<WFRoundSetupClientSystem>().State!.OwnLobby, Is.Null);
            window.Close();
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Hosts can find invitations even when nobody is eligible, with an explicit disabled-state explanation.</summary>
    [Test]
    public async Task EmptyInvitationsRemainVisibleAndExplainEligibility()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
        var id = state.OwnLobby!.Value;
        Assert.That(state.InviteTargets, Is.Empty);
        WFRoundSetupWindow window = null!;
        Button admission = null!;
        await pair.Client.WaitPost(() =>
        {
            window = new WFRoundSetupWindow();
            window.FitTo(new Vector2(800, 600));
            window.OpenCentered();
        });
        await pair.RunTicksSync(2);
        await pair.Client.WaitPost(() => admission = Buttons(Named<Control>(window, "RoundSetupSections"), "admission").Single());
        await Click(pair, admission);
        await pair.Client.WaitAssertion(() =>
        {
            var invite = Named<Button>(window, "CrewSendInvitation");
            var target = Named<WFRoundSetupOptionButton>(window, "CrewInviteTarget");
            Assert.That(invite.VisibleInTree, Is.True);
            Assert.That(invite.Disabled, Is.True);
            Assert.That(target.Disabled, Is.True);
            Assert.That(invite.Text, Is.EqualTo(Loc.GetString("wf-round-setup-send-invitation")));
            Assert.That(invite.ToolTip, Is.EqualTo(Loc.GetString("wf-round-setup-invite-none-help")));
            Assert.That(Labels(window), Does.Contain(invite.ToolTip));
            var scroll = Named<ScrollContainer>(window, "RoundSetupPageScroll");
            Assert.That(invite.GlobalPosition.Y + invite.Height, Is.LessThanOrEqualTo(scroll.GlobalPosition.Y + scroll.Height));
            AssertFits(window, new Vector2(800, 600));
            window.Close();
        });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.CleanReturnAsync();
    }

    /// <summary>The toolbar toggles crew setup, follows window closure and survives HUD layout changes.</summary>
    [Test]
    public async Task GameplayToolbarTogglesCrewSetup()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        var originalLayout = pair.Client.CfgMan.GetCVar(CCVars.UILayout);
        await pair.Server.WaitPost(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            ticker.StartRound(force: true);
            ticker.JoinAsObserver(pair.Player!);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await PoolManager.WaitUntil(pair.Client, () => ui.GetActiveUIWidgetOrNull<GameTopMenuBar>() != null, maxTicks: 180);
        MenuButton button = null!;
        foreach (var layout in new[] { "Default", "Separated" })
        {
            await pair.Client.WaitPost(() => pair.Client.CfgMan.SetCVar(CCVars.UILayout, layout));
            await pair.RunTicksSync(2);
            await pair.Client.WaitAssertion(() =>
            {
                button = Named<MenuButton>(ui.ActiveScreen!, "CrewSetupButton");
                Assert.That(button.VisibleInTree, Is.True);
                Assert.That(button.Icon, Is.Not.Null);
                Assert.That(Descendants(button).OfType<TextureRect>().Single().TextureScale, Is.EqualTo(Vector2.One));
                Assert.That(button.ToolTip, Does.StartWith("Crew Setup"));
                Assert.That(Content.Client.UserInterface.BoundKeyHelper.ShortKeyName(button.BoundKey), Is.EqualTo(" "));
                Assert.That(button.Pressed, Is.False);
            });
            await Click(pair, button);
            await pair.Client.WaitAssertion(() =>
            {
                var window = Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>().Single();
                Assert.That(window.Title, Is.EqualTo("Crew Setup"));
                Assert.That(button.Pressed, Is.True);
                window.Close();
                Assert.That(button.Pressed, Is.False);
            });
            await pair.Client.WaitPost(() => ui.GetUIController<WFRoundSetupUIController>().OpenWindow());
            await pair.Client.WaitAssertion(() => Assert.That(button.Pressed, Is.True));
            await Click(pair, button);
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>(), Is.Empty);
                Assert.That(button.Pressed, Is.False);
            });
        }
        await pair.Client.WaitPost(() => pair.Client.CfgMan.SetCVar(CCVars.UILayout, originalLayout));
        await pair.Server.WaitPost(() => pair.Server.System<GameTicker>().RestartRound());
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }

    /// <summary>Compact rows open crew details without changing membership or readiness.</summary>
    [Test]
    public async Task CompactCrewRowOpensDetailsAndKeepsWelcomeAtBottom()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = WFRoundScenario.Ship, Title = "Mercy medical crew",
        });
        var id = state.OwnLobby!.Value;
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        Button row = null!;
        await pair.Client.WaitAssertion(() =>
        {
            var screen = (LobbyGui) ui.ActiveScreen!;
            row = Named<WFRoundSetupCrewRow>(screen, "RoundSetupCrewRow" + id);
            Assert.That(row.Height, Is.EqualTo(34));
            Assert.That(Labels(row), Does.Contain("Mercy medical crew"));
            Assert.That(screen.ServerInfo.Parent!.Parent!.Height -
                (screen.ServerInfo.Parent.Position.Y + screen.ServerInfo.Parent.Height), Is.LessThanOrEqualTo(1));
        });
        await Click(pair, row);
        await pair.Client.WaitAssertion(() =>
        {
            var window = Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>().Single();
            Assert.That(window.Title, Is.EqualTo("Mercy medical crew"));
            Assert.That(Descendants(row).OfType<TextureRect>().Any(icon => icon.Stretch == TextureRect.StretchMode.Scale && icon.Width >= 24), Is.True);
            Assert.That(pair.Client.System<ClientGameTicker>().AreWeReady, Is.False);
            var snapshot = pair.Client.System<WFRoundSetupClientSystem>().State!;
            Assert.That(snapshot.OwnLobby, Is.EqualTo(id));
            var original = snapshot.Lobbies;
            snapshot.Lobbies = original.Append(new WFRoundSetupLobby
            {
                Id = id + 1000, Title = "Visiting crew", Scenario = WFRoundScenario.Ship, Access = WFLobbyAccess.Public,
            }).ToArray();
            window.ShowLobby(id + 1000);
            Assert.That(Labels(window), Does.Contain("Visiting crew"), "An existing member can inspect other crews without leaving.");
            Assert.That(Buttons(window, "join").Single().Disabled, Is.True);
            Assert.That(snapshot.OwnLobby, Is.EqualTo(id));
            snapshot.Lobbies = original;
            window.Close();
        });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.CleanReturnAsync();
    }

    /// <summary>Opening Join is inert; preparation is first and fixed actions remain accessible at every size.</summary>
    [TestCase(800, 600)]
    [TestCase(1280, 720)]
    [TestCase(1920, 1080)]
    public async Task SetupFlowFitsViewportAndBrowsingDoesNotCreate(int width, int height)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var originalNetworks = await EnableCrashlanded(pair);
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
            Assert.That(Buttons(window, "join-home"), Is.Empty);
            Assert.That(Descendants(Named<Control>(window, "RoundSetupNavigation")).OfType<Button>().Count(), Is.EqualTo(2));
            button = Buttons(window, "flow-create").Single();
        });
        await Click(pair, button);
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, viewport);
            Assert.That(Labels(window), Does.Contain(Loc.GetString("wf-round-setup-choose-scenario")));
            Assert.That(Descendants(window).OfType<Button>().Count(control => control.Name?.StartsWith("RoundSetupCreate") == true), Is.EqualTo(4));
            var faction = Named<Button>(window, "RoundSetupCreateFactionShip");
            Assert.That(faction.Disabled, Is.True, "The empty test map offers no faction jobs.");
            Assert.That(faction.ToolTip, Is.EqualTo(Loc.GetString("wf-round-setup-faction-unavailable")));
            Assert.That(Descendants(window).Any(control => control.Name == "RoundSetupCreateNormal"), Is.False);
            Assert.That(pair.Client.System<WFRoundSetupClientSystem>().State!.OwnLobby, Is.Null);
            button = Named<Button>(window, "RoundSetupCreateCrashlanded");
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
            Assert.That(Named<Button>(window, "RoundSetupReady").Disabled, Is.True);
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
            Assert.That(Named<Button>(window, "RoundSetupReady").Disabled, Is.False);
            Assert.That(Named<WFRoundSetupOptionButton>(window, "RoundSetupCrewType").SelectedId,
                Is.EqualTo((int) WFRoundScenario.Crashlanded));
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
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, originalNetworks));
        await pair.CleanReturnAsync();
    }

    /// <summary>Pregame READY offers crew setup by default; legacy leaves a crew before readying with saved preferences.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task PregameReadyOffersCrewOrLegacy(bool alreadyInCrew)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        var state = await Request(pair, new WFRoundSetupRequest(alreadyInCrew ? WFRoundSetupAction.Create : WFRoundSetupAction.Refresh)
        { Scenario = WFRoundScenario.Ship });
        var own = state.OwnLobby;
        Assert.That(own.HasValue, Is.EqualTo(alreadyInCrew));
        Button ready = null!;
        Button choice = null!;
        WFRoundJoinChoiceWindow chooser = null!;
        await pair.Client.WaitAssertion(() =>
        {
            var lobby = (LobbyGui) ui.ActiveScreen!;
            Assert.That(lobby.ReadyButton.ToggleMode, Is.False);
            Assert.That(lobby.RoundSetupButton.VisibleInTree, Is.True);
            ready = lobby.ReadyButton;
        });
        await Click(pair, ready);
        await pair.Client.WaitAssertion(() =>
        {
            chooser = Descendants(ui.WindowRoot).OfType<WFRoundJoinChoiceWindow>().Single();
            Assert.That(chooser.Title, Is.EqualTo(Loc.GetString("wf-round-setup-ready-choice-title")));
            Assert.That(pair.Client.System<ClientGameTicker>().AreWeReady, Is.False);
            Assert.That(pair.Client.System<WFRoundSetupClientSystem>().State!.OwnLobby, Is.EqualTo(own));
            Assert.That(Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>(), Is.Empty);
            Assert.That(Descendants(ui.WindowRoot).OfType<PickerWindow>(), Is.Empty);
            chooser.Close();
        });
        await Click(pair, ready);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(pair.Client.System<ClientGameTicker>().AreWeReady, Is.False, "Dismissing the choice must not ready the player.");
            chooser = Descendants(ui.WindowRoot).OfType<WFRoundJoinChoiceWindow>().Single();
            choice = Named<Button>(chooser, "RoundJoinCrew");
            var legacy = Named<Button>(chooser, "RoundJoinStandard");
            Assert.That(choice.Text, Is.EqualTo(Loc.GetString("wf-round-setup-ready-crew-default")));
            Assert.That(choice.HasStyleClass("ButtonAccept"), Is.True);
            Assert.That(choice.GlobalPosition.Y, Is.LessThan(legacy.GlobalPosition.Y));
            Assert.That(legacy.Text, Is.EqualTo(Loc.GetString("wf-round-setup-ready-standard-legacy")));
            Assert.That(legacy.Label.ClipText, Is.False);
        });
        await Click(pair, choice);
        await pair.Client.WaitAssertion(() =>
        {
            var crews = Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>().Single();
            Assert.That(Descendants(ui.WindowRoot).OfType<WFRoundJoinChoiceWindow>(), Is.Empty);
            Assert.That(pair.Client.System<ClientGameTicker>().AreWeReady, Is.False);
            Assert.That(pair.Client.System<WFRoundSetupClientSystem>().State!.OwnLobby, Is.EqualTo(own));
            crews.Close();
        });
        // Allow the crew browser refresh to clear the server request throttle.
        await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
        await Click(pair, ready);
        await pair.Client.WaitPost(() => choice = Named<Button>(ui.WindowRoot, "RoundJoinStandard"));
        await Click(pair, choice);
        await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(pair.Client.System<ClientGameTicker>().AreWeReady, Is.True);
            Assert.That(pair.Client.System<WFRoundSetupClientSystem>().State!.OwnLobby, Is.Null);
            Assert.That(Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>(), Is.Empty);
            Assert.That(Descendants(ui.WindowRoot).OfType<WFRoundJoinChoiceWindow>(), Is.Empty);
            Assert.That(Descendants(ui.WindowRoot).OfType<PickerWindow>(), Is.Empty);
            Assert.That(ready.Text, Is.EqualTo(Loc.GetString("wf-round-setup-home-unready")));
            Assert.That(ready.HasStyleClass("ButtonColorGreen"), Is.True);
        });
        await Click(pair, ready);
        await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(pair.Client.System<ClientGameTicker>().AreWeReady, Is.False);
            Assert.That(ready.HasStyleClass("ButtonColorRed"), Is.True);
            Assert.That(Descendants(ui.WindowRoot).OfType<WFRoundJoinChoiceWindow>(), Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Late join offers crews first and opens legacy joining directly to stations.</summary>
    [Test]
    public async Task MidroundHomeOffersCrewAndStandard()
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
        Button choice = null!;
        await pair.Client.WaitAssertion(() =>
        {
            var chooser = Descendants(ui.WindowRoot).OfType<WFRoundJoinChoiceWindow>().Single();
            choice = Named<Button>(chooser, "RoundJoinCrew");
            Assert.That(choice.HasStyleClass("ButtonAccept"), Is.True);
            Assert.That(Descendants(ui.WindowRoot).OfType<PickerWindow>(), Is.Empty);
        });
        await Click(pair, choice);
        await pair.Client.WaitAssertion(() =>
        {
            var crews = Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>().Single();
            Assert.That(Descendants(ui.WindowRoot).OfType<WFRoundJoinChoiceWindow>(), Is.Empty);
            Assert.That(pair.Client.System<WFRoundSetupClientSystem>().State!.OwnLobby, Is.Null);
            crews.Close();
        });
        await Click(pair, join);
        await pair.Client.WaitPost(() => choice = Named<Button>(ui.WindowRoot, "RoundJoinStandard"));
        await Click(pair, choice);
        await pair.Client.WaitAssertion(() =>
        {
            var picker = Descendants(ui.WindowRoot).OfType<PickerWindow>().Single();
            // The original picker stays intact, including its route onto player ship crews.
            Assert.That(Descendants(picker).OfType<StationOrCrewLargeControl>().Count(), Is.EqualTo(1));
            Assert.That(Named<Button>(picker, "CrewTabButton").Visible, Is.True);
            Assert.That(Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>(), Is.Empty);
            picker.Close();
        });
        await pair.Server.WaitPost(() => pair.Server.System<GameTicker>().RestartRound());
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }

    /// <summary>The repository lobby keeps joining prominent and footer labels readable at supported sizes.</summary>
    [TestCase(800, 600)]
    [TestCase(1024, 768)]
    [TestCase(1090, 720)]
    [TestCase(1280, 720)]
    [TestCase(1920, 1080)]
    public async Task HomeLayoutKeepsPrimaryActionsReachable(int width, int height)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        await pair.Client.WaitAssertion(() =>
        {
            var screen = (LobbyGui) pair.Client.ResolveDependency<IUserInterfaceManager>().ActiveScreen!;
            var viewport = new Vector2(width, height);
            Assert.That(Descendants(screen.RightSide).OfType<TabContainer>(), Is.Empty);
            screen.Measure(viewport);
            screen.Arrange(UIBox2.FromDimensions(Vector2.Zero, viewport));
            foreach (var control in new Control[] { screen.ReadyButton, screen.CustomizeButton,
                         screen.RoundSetupButton, screen.RightSide, screen.Chat })
            {
                Assert.That(control.VisibleInTree, Is.True, control.Name);
                Assert.That(control.Size.X, Is.GreaterThan(40), control.Name);
                Assert.That(control.Size.Y, Is.GreaterThan(20), control.Name);
                Assert.That(control.GlobalPosition.X, Is.GreaterThanOrEqualTo(0), control.Name);
                Assert.That(control.GlobalPosition.Y, Is.GreaterThanOrEqualTo(0), control.Name);
                Assert.That(control.GlobalPosition.X + control.Width, Is.LessThanOrEqualTo(width + 1), control.Name);
                Assert.That(control.GlobalPosition.Y + control.Height, Is.LessThanOrEqualTo(height + 1), control.Name);
            }
            Assert.That(screen.RightSide.GlobalPosition.X, Is.GreaterThan(screen.ReadyButton.GlobalPosition.X));
            Assert.That(screen.ServerInfo.VisibleInTree, Is.True);
            Assert.That(screen.ServerInfo.GlobalPosition.Y, Is.GreaterThan(screen.RoundSetupButton.GlobalPosition.Y));
            Assert.That(Named<Control>(screen, "ActiveCrews").GlobalPosition.Y,
                Is.GreaterThanOrEqualTo(screen.LeaveButton.GlobalPosition.Y + screen.LeaveButton.Height));
            Assert.That(screen.ServerInfo.GlobalPosition.X, Is.LessThan(screen.Chat.GlobalPosition.X));
            foreach (var button in new[] { screen.ReadyButton, screen.RoundSetupButton, screen.CustomizeButton })
                Assert.That(button.Label.Align, Is.EqualTo(Label.AlignMode.Left));
            Assert.That(screen.ReadyButton.HasStyleClass("LobbyNavPrimary"), Is.True);
            Assert.That(screen.RoundSetupButton.HasStyleClass("WFLobbyCrewAction"), Is.True);
            Assert.That(screen.RoundSetupButton.GlobalPosition.Y + screen.RoundSetupButton.Height,
                Is.LessThanOrEqualTo(screen.ReadyButton.GlobalPosition.Y));
            Assert.That(Named<Control>(screen, "CharacterHero").Visible, Is.EqualTo(width >= 1090));
            Assert.That(Named<Control>(screen.CharacterPreview, "Loaded").SetWidth, Is.EqualTo(300));
            Assert.That(screen.RoundSetupButton.Height, Is.EqualTo(screen.ReadyButton.Height));
            Assert.That(Descendants(screen).Any(control => control.Name == "HomeBackdrop"), Is.False);
            Assert.That(Named<Control>(screen, "SkinScanlines").Visible, Is.False);
            Assert.That(screen.CharacterPreview.CharacterSetupButton.Visible, Is.False, "Only one character edit action is shown.");
            var changelog = Descendants(Named<Control>(screen, "LinkBanner")).OfType<ChangelogButton>().Single();
            changelog.Text = Loc.GetString("changelog-button-new-entries");
            screen.Measure(viewport);
            screen.Arrange(UIBox2.FromDimensions(Vector2.Zero, viewport));
            Assert.That(changelog.Label.ClipText, Is.False);
            Assert.That(changelog.Label.Width, Is.GreaterThanOrEqualTo(changelog.Label.DesiredSize.X));
            Assert.That(changelog.Label.Height, Is.GreaterThanOrEqualTo(changelog.Label.DesiredSize.Y));
            var footer = Named<Control>(screen, "LobbyUtilities");
            Assert.That(changelog.GlobalPosition.Y, Is.GreaterThanOrEqualTo(footer.GlobalPosition.Y));
            Assert.That(changelog.GlobalPosition.Y + changelog.Height, Is.LessThanOrEqualTo(height + 1));
            var vote = new VotePopup(new VoteManager.ActiveVote(1)
            {
                Title = "Continue this round?",
                Initiator = "Test voter",
                EndTime = TimeSpan.FromHours(1),
                Entries = new[] { new VoteManager.VoteEntry("Continue"), new VoteManager.VoteEntry("Restart") },
            });
            vote.UpdateData();
            screen.VoteContainer.AddChild(vote);
            screen.Measure(viewport);
            screen.Arrange(UIBox2.FromDimensions(Vector2.Zero, viewport));
            Assert.That(vote.VisibleInTree, Is.True, "Votes remain visible alongside chat.");
            Assert.That(vote.GlobalPosition.X + vote.Width, Is.LessThanOrEqualTo(width + 1), "Votes fit the communication dock.");
            screen.VoteContainer.RemoveChild(vote);
            vote.Dispose();
            if (screen.VoteContainer.ChildCount == 0)
                Assert.That(screen.Chat.VisibleInTree, Is.True, "Chat returns when the last vote ends.");

        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Members return to their crew and character editing clears the crew's ready state.</summary>
    [Test]
    public async Task HomeReturnsToCrewAndCharacterEditingClearsReadiness()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var originalNetworks = await EnableCrashlanded(pair);
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Crashlanded });
        var id = state.OwnLobby!.Value;
        var job = state.Jobs.First();
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = job.Id, Station = job.Station });
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = id, Ready = true });
        Assert.That(state.Lobbies.Single(lobby => lobby.Id == id).Members.Single().Ready, Is.True);
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        LobbyGui screen = null!;
        await pair.Client.WaitAssertion(() =>
        {
            screen = (LobbyGui) ui.ActiveScreen!;
            Assert.That(screen.ReadyButton.Text, Is.EqualTo(Loc.GetString("wf-round-setup-home-unready")));
            Assert.That(screen.RoundSetupButton.Disabled, Is.False);
        });
        await Click(pair, screen.RoundSetupButton);
        await pair.Client.WaitAssertion(() =>
        {
            var window = Descendants(ui.WindowRoot).OfType<WFRoundSetupWindow>().Single();
            Assert.That(Named<Button>(window, "RoundSetupReady").HasStyleClass("ButtonColorGreen"), Is.True);
            window.Close();
        });
        await Click(pair, screen.CustomizeButton);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(state.Lobbies.Single(lobby => lobby.Id == id).Members.Single().Ready, Is.False);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(screen.CharacterSetupState.Visible, Is.True);
            Assert.That(screen.RightSide.Visible, Is.False);
            screen.SwitchState(LobbyGui.LobbyGuiState.Default);
            Assert.That(screen.RightSide.Visible, Is.True);
        });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, originalNetworks));
        await pair.CleanReturnAsync();
    }

    /// <summary>Confirming a crew closes setup only after the server marks the player ready.</summary>
    [Test]
    public async Task CrewConfirmationClosesSetupAndReadiesHome()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var originalNetworks = await EnableCrashlanded(pair);
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = WFRoundScenario.Crashlanded, Title = "The Wayfarers",
        });
        var id = state.OwnLobby!.Value;
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = "Contractor" });
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        WFRoundSetupWindow window = null!;
        Button confirm = null!;
        await pair.Client.WaitPost(() =>
        {
            window = new WFRoundSetupWindow();
            window.OpenCentered();
            window.ShowLobby(id);
            confirm = Named<Button>(window, "RoundSetupReady");
        });
        await pair.RunTicksSync(pair.SecondsToTicks(0.3f));
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Labels(window), Does.Contain("The Wayfarers"));
            Assert.That(confirm, Is.Not.InstanceOf<CheckBox>());
            Assert.That(Buttons(window, "leave").Single().HasStyleClass("ButtonColorRed"), Is.True);
        });
        await Click(pair, confirm);
        await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(window.IsOpen, Is.False);
            Assert.That(pair.Client.System<ClientGameTicker>().AreWeReady, Is.True);
            Assert.That(((LobbyGui) ui.ActiveScreen!).ReadyButton.HasStyleClass("ButtonColorGreen"), Is.True);
        });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, originalNetworks));
        await pair.CleanReturnAsync();
    }

    /// <summary>Ship browsing loads the real preview without selecting or buying, and releases its map when leaving.</summary>
    [TestCase(800, 600)]
    [TestCase(1090, 720)]
    [TestCase(1280, 720)]
    [TestCase(1920, 1080)]
    public async Task ShipPreviewLoadsBeforeSelectionAndReleasesAfterNavigation(int width, int height)
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
            window.FitTo(new Vector2(width, height));
            window.OpenCentered();
            window.ShowLobby(lobbyId);
        });
        await pair.RunTicksSync(2);
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, new Vector2(width, height));
            shipPage = Buttons(Named<Control>(window, "RoundSetupSections"), "ship").Single();
        });
        await Click(pair, shipPage);
        ShipPreviewControl preview = null!;
        OptionButton selector = null!;
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, new Vector2(width, height));
            preview = Named<ShipPreviewControl>(window, "RoundSetupShipPreview");
            selector = Named<OptionButton>(window, "RoundSetupShipSelector");
            Assert.That(preview.HasPreview, Is.False, "Opening ship options must not choose a vessel implicitly.");
            Assert.That(Named<ScrollContainer>(window, "RoundSetupPageScroll").Visible, Is.False);
            Assert.That(Descendants(Named<Control>(window, "RoundSetupShipPage")).OfType<ScrollContainer>(), Is.Empty);
            var navigation = Named<Control>(window, "RoundSetupNavigation");
            Assert.That(Buttons(navigation, "join-home"), Is.Empty);
            Assert.That(Buttons(navigation, "flow-browse"), Is.Empty);
            Assert.That(Buttons(navigation, "flow-create"), Is.Empty);
            Assert.That(Named<Control>(window, "RoundSetupSections").GlobalPosition.Y, Is.LessThan(preview.GlobalPosition.Y));
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
        await PoolManager.WaitUntil(pair.Client, () => Descendants(selector).OfType<RichTextLabel>()
            .Any(label => label.GetFormattedMessage()?.ToString().Contains(vessel.Name) == true), maxTicks: 30);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(preview.TileCount, Is.Positive, "The ship preview must load actual grid tiles.");
            Assert.That(preview.GridSize.X, Is.Positive);
            Assert.That(preview.GridSize.Y, Is.Positive);
            Assert.That(maps.GetAllMapIds().Except(originalMaps).Count(), Is.EqualTo(1));
        });
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(state.Lobbies.Single(lobby => lobby.Id == lobbyId).Vessel, Is.EqualTo(vessel.Id),
            "Choosing from the dropdown commits the ship immediately.");
        Assert.That(state.HostBalance, Is.EqualTo(balance), "Selecting a ship must not charge before Finish.");

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

    /// <summary>The ship wizard fits without scrolling and finishes with explicit readiness and purchase consent.</summary>
    [TestCase(800, 600)]
    [TestCase(1280, 720)]
    [TestCase(1920, 1080)]
    public async Task ShipWizardUsesNextAndFinish(int width, int height)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
        var id = state.OwnLobby!.Value;
        WFRoundSetupWindow window = null!;
        Button next = null!;
        await pair.Client.WaitPost(() =>
        {
            window = new WFRoundSetupWindow();
            window.FitTo(new Vector2(width, height));
            window.OpenCentered();
            window.ShowLobby(id);
        });
        await pair.RunTicksSync(2);
        await pair.Client.WaitAssertion(() =>
        {
            AssertFits(window, new Vector2(width, height));
            var scroll = Named<ScrollContainer>(window, "RoundSetupPageScroll");
            var body = Named<Control>(window, "RoundSetupPage");
            Assert.That(scroll.VScrollEnabled, Is.False);
            Assert.That(body.DesiredSize.Y, Is.LessThanOrEqualTo(scroll.Height + 1), "Preparation must fit, not clip.");
            next = Named<Button>(window, "RoundSetupNext");
            Assert.That(next.Disabled, Is.True);
            Assert.That(next.ToolTip, Is.EqualTo(Loc.GetString("wf-round-setup-ready-needs-job")));
            Assert.That(Descendants(window).Any(control => control.Name == "RoundSetupReady"), Is.False);
            var picker = Named<OptionButton>(window, "RoundSetupPersonalJob");
            Assert.That(picker.ItemCount, Is.EqualTo(state.RoleJobs.Length + 1));
            Assert.That(Descendants(picker.OptionsScroll).OfType<Button>().Where(button => button.Disabled)
                .All(button => !string.IsNullOrWhiteSpace(button.ToolTip)), Is.True);
        });
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = "Contractor" });
        await pair.Client.WaitPost(() =>
        {
            var title = Named<LineEdit>(window, "RoundSetupPersonalTitle");
            title.SetText("Captain", invokeEvent: true);
            next = Named<Button>(window, "RoundSetupNext");
        });
        await Click(pair, next);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(state.Lobbies.Single(lobby => lobby.Id == id).Members.Single().Title, Is.EqualTo("Captain"));
        await pair.Client.WaitAssertion(() => Assert.That(Named<Control>(window, "RoundSetupShipPage").Visible, Is.True));
        var vessel = state.Vessels.OrderBy(entry => entry.Price).First();
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectVessel) { LobbyId = id, Vessel = vessel.Id });
        await pair.Client.WaitPost(() =>
        {
            Named<LineEdit>(window, "RoundSetupShipName").SetText("Wayfarer", invokeEvent: true);
            next = Named<Button>(window, "RoundSetupNext");
        });
        await Click(pair, next);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(state.Lobbies.Single(lobby => lobby.Id == id).ShipName, Is.EqualTo("Wayfarer"));
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Labels(window), Does.Contain(Loc.GetString("wf-round-setup-roles-empty-host")));
            next = Named<Button>(window, "RoundSetupNext");
        });
        await Click(pair, next);
        await pair.Client.WaitAssertion(() =>
        {
            var finish = Named<Button>(window, "RoundSetupReady");
            Assert.That(finish.Text, Is.EqualTo(Loc.GetString("wf-round-setup-payment-approve")));
            Assert.That(Named<Button>(window, "RoundSetupSave").Disabled, Is.True);
            Assert.That(Named<Button>(window, "RoundSetupSave").ToolTip, Is.Not.Empty);
            AssertFits(window, new Vector2(width, height));
            Named<LineEdit>(window, "RoundSetupSaveName").SetText("Wizard preset", invokeEvent: true);
            next = Named<Button>(window, "RoundSetupSave");
        });
        await Click(pair, next);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(state.SavedSetups.Single().JobTitle, Is.EqualTo("Captain"));
        var savedId = state.SavedSetups.Single().Id;
        await pair.Client.WaitPost(() => next = Named<Button>(window, "RoundSetupReady"));
        await Click(pair, next);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(state.Lobbies.Single(lobby => lobby.Id == id).Members.Single().Ready, Is.True);
        await pair.Client.WaitAssertion(() => Assert.That(window.IsOpen, Is.False));
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.DeleteSetup) { SavedId = savedId });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.CleanReturnAsync();
    }

    /// <summary>Saved scenarios stay behind an explicit action and open compact rows into full details.</summary>
    [Test]
    public async Task SavedBrowserAndDescriptionFlow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = WFRoundScenario.Ship, Title = "Mercy", Description = "Medical rescue crew.",
        });
        var id = state.OwnLobby!.Value;
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SaveSetup) { LobbyId = id, SaveName = "Rescue template" });
        var savedId = state.SavedSetups.Single().Id;
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
        WFRoundSetupWindow window = null!;
        Button button = null!;
        await pair.Client.WaitPost(() =>
        {
            window = new WFRoundSetupWindow();
            window.FitTo(new Vector2(800, 600));
            window.OpenCentered();
            window.OpenCreate();
        });
        await pair.RunTicksSync(2);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Labels(window), Does.Not.Contain("Rescue template"));
            button = Named<Button>(window, "RoundSetupLoadSaved");
        });
        await Click(pair, button);
        await pair.Client.WaitAssertion(() =>
        {
            button = Named<Button>(window, "RoundSetupSavedRow" + savedId);
            Assert.That(button.Height, Is.EqualTo(40));
            Assert.That(Labels(window), Does.Not.Contain("Medical rescue crew."));
        });
        await Click(pair, button);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Labels(window), Does.Contain("Medical rescue crew."));
            button = Buttons(window, "load-setup").Single();
        });
        await Click(pair, button);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        id = state.OwnLobby!.Value;
        Assert.That(state.Lobbies.Single(lobby => lobby.Id == id).Description, Is.EqualTo("Medical rescue crew."));
        await pair.Client.WaitPost(() => button = Buttons(Named<Control>(window, "RoundSetupSections"), "admission").Single());
        await Click(pair, button);
        await pair.Client.WaitPost(() =>
        {
            Named<LineEdit>(window, "RoundSetupSaveName").SetText("Rescue template", invokeEvent: true);
            Named<TextEdit>(window, "RoundSetupDescription").InsertAtCursor(new string('X', 513));
        });
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Named<Button>(window, "RoundSetupSave").Disabled, Is.True);
            Assert.That(Named<Button>(window, "RoundSetupSave").ToolTip, Does.Contain("512"));
            window.Close();
        });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.DeleteSetup) { SavedId = savedId });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
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
                var readout = Named<Label>(hud, "CrashCountdownClock");
                Assert.That(readout.ClipText, Is.False);
                Assert.That(readout.Width, Is.GreaterThanOrEqualTo(readout.DesiredSize.X));
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
    private static async Task<bool> EnableCrashlanded(TestPair pair)
    {
        var previous = false;
        await pair.Server.WaitPost(() =>
        {
            previous = pair.Server.CfgMan.GetCVar(PlanetCVars.PlanetNetworks);
            pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true);
        });
        return previous;
    }

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
        if (scroll.Visible)
        {
            Assert.That(scroll.Height, Is.GreaterThan(100), "Leave usable space for the scrolling page.");
            Assert.That(page.Width, Is.LessThanOrEqualTo(scroll.Width + 1));
        }
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
    internal static async Task Click(TestPair pair, BaseButton button)
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
        await pair.Client.WaitPost(() =>
        {
            var system = pair.Client.System<WFRoundSetupClientSystem>();
            request.PaymentRevision ??= system.State?.Payment?.Revision;
            system.Send(request);
        });
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
