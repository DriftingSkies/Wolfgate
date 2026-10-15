using System.Linq;
using Content.Client.GameTicking.Managers;
using Content.Client._NF.LateJoin.Windows;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Console;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private enum SetupView : byte { Join, Browse, Create, MyLobby }
    private enum LobbyPage : byte { Overview, Ship, Admission, Roles, Character }

    private readonly BoxContainer _navigation = Row();
    private readonly BoxContainer _sidebar = Column(8);
    private readonly BoxContainer _footer = Column(8);
    private readonly ScrollContainer _sidebarScroll = new() { HScrollEnabled = false, VerticalExpand = true, SetWidth = 260 };
    private readonly ScrollContainer _pageScroll = new() { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
    private SetupView _view;
    private LobbyPage _lobbyPage;
    private int? _focusedLobby;
    private bool _pendingLobbyFocus;
    private bool _creatingLobby;
    private bool _initialViewResolved;
    private bool _manualNavigation;
    private bool _compact;
    private bool _rendering;
    private float _lastLayoutWidth = 1600;
    private bool _layoutRefreshPending;

    private float ChoiceWidth => Math.Max(180, _lastLayoutWidth - (_compact ? 80 : 380));

    /// <summary>Starts the primary join flow without choosing a scenario or changing readiness.</summary>
    public void ShowJoin()
    {
        _manualNavigation = true;
        _pendingLobbyFocus = false;
        Navigate(SetupView.Join);
    }

    /// <summary>Opens a lobby directly from the game's lobby list.</summary>
    public void ShowLobby(int lobbyId)
    {
        _manualNavigation = true;
        _focusedLobby = lobbyId;
        _pendingLobbyFocus = true;
        _view = _state?.OwnLobby == lobbyId ? SetupView.MyLobby : SetupView.Browse;
        _lobbyPage = LobbyPage.Overview;
        Redraw();
    }

    /// <summary>Shows the available starts; only an explicit Create action opens a lobby.</summary>
    public void OpenCreate() => Navigate(SetupView.Create);

    public void ShowScenarios() => OpenCreate();

    private void InitializeLayout()
    {
        _navigation.Name = "RoundSetupNavigation";
        _sidebar.Name = "RoundSetupSidebar";
        _body.Name = "RoundSetupPage";
        _footer.Name = "RoundSetupFooter";
        _pageScroll.Name = "RoundSetupPageScroll";
        var layout = Column(12);
        Contents.AddChild(layout);
        layout.AddChild(_navigation);
        layout.AddChild(_message);
        _sidebarScroll.AddChild(_sidebar);
        _pageScroll.AddChild(_body);
        var workspace = Row(_sidebarScroll, _pageScroll);
        workspace.VerticalExpand = true;
        layout.AddChild(workspace);
        layout.AddChild(_footer);
    }

    protected override void Resized()
    {
        base.Resized();
        var compact = Width < 900;
        if (_compact == compact && Math.Abs(_lastLayoutWidth - Width) < 1)
            return;
        _compact = compact;
        _lastLayoutWidth = Width;
        _layoutRefreshPending = true;
    }

    private void Redraw()
    {
        if (_state != null && !_rendering)
            RenderState(_state);
    }

    private void Navigate(SetupView view)
    {
        _manualNavigation = true;
        _view = view;
        _pendingLobbyFocus = false;
        _pageScroll.VScroll = 0;
        Redraw();
    }

    private void NavigateLobby(LobbyPage page)
    {
        _lobbyPage = page;
        _pageScroll.VScroll = 0;
        Redraw();
    }

    /// <summary>Shows one task at a time while retaining unsaved form values.</summary>
    private void RenderState(WFRoundSetupState state)
    {
        if (_rendering)
            return;
        _rendering = true;
        try
        {
            var own = state.Lobbies.FirstOrDefault(lobby => lobby.Id == state.OwnLobby);
            if (!_initialViewResolved)
            {
                _initialViewResolved = true;
                if (!_manualNavigation)
                    _view = own == null ? SetupView.Join : SetupView.MyLobby;
            }
            if (_pendingLobbyFocus)
            {
                _pendingLobbyFocus = false;
                _view = own?.Id == _focusedLobby ? SetupView.MyLobby : SetupView.Browse;
                _lobbyPage = LobbyPage.Overview;
            }
            if (_view == SetupView.MyLobby && own == null)
                _view = SetupView.Join;
            if (_view == SetupView.Create && own != null)
                _view = SetupView.MyLobby;
            _body.RemoveAllChildren();
            _navigation.RemoveAllChildren();
            _sidebar.RemoveAllChildren();
            _footer.RemoveAllChildren();
            _sidebarScroll.Visible = false;
            if (_view != SetupView.Join)
            {
                _navigation.AddChild(Button("join-home", ShowJoin));
                _navigation.AddChild(NavigationButton("flow-browse", () => Navigate(SetupView.Browse), _view == SetupView.Browse));
                if (own != null)
                    _navigation.AddChild(NavigationButton("flow-my-lobby", () => Navigate(SetupView.MyLobby), _view == SetupView.MyLobby));
                _navigation.AddChild(new Control { HorizontalExpand = true });
                if (_view == SetupView.Browse && own == null)
                    _navigation.AddChild(Button("flow-create", OpenCreate, !state.CanJoin));
                if (_view == SetupView.Browse)
                    _navigation.AddChild(Button("refresh", () => Send(WFRoundSetupAction.Refresh)));
            }
            switch (_view)
            {
                case SetupView.Join: BuildJoin(state); break;
                case SetupView.Create: BuildScenarios(state); break;
                case SetupView.MyLobby when own != null: BuildOwnLobby(state, own); break;
                default: BuildBrowser(state); break;
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private static Button NavigationButton(string key, Action action, bool selected, bool disabled = false)
    {
        var button = Button(key, action, disabled);
        button.ToggleMode = true;
        button.Pressed = selected;
        button.MinHeight = 40;
        return button;
    }

    private BoxContainer FlowRow(params Control[] controls)
    {
        var row = _compact ? Column(10) : Row();
        foreach (var control in controls)
            row.AddChild(control);
        return row;
    }

    private void BuildJoin(WFRoundSetupState state)
    {
        _body.AddChild(Heading("join-heading"));
        var normal = Column(16);
        normal.AddChild(Heading("join-normal"));
        normal.AddChild(Description(state.InRound ? "join-normal-round" : "join-normal-pregame"));
        var normalButton = Button("join-normal", JoinNormally, !state.CanJoin || state.OwnLobby != null);
        normalButton.Name = "RoundSetupJoinNormally";
        normalButton.MinHeight = 64;
        normal.AddChild(normalButton);
        var groups = Column(16);
        groups.AddChild(Heading("join-lobby"));
        groups.AddChild(Description("join-lobby-help"));
        var browse = Button("join-lobby", () => Navigate(SetupView.Browse));
        browse.Name = "RoundSetupJoinLobby";
        browse.MinHeight = 64;
        groups.AddChild(browse);
        _body.AddChild(FlowRow(Panel(normal), Panel(groups)));
        if (state.OwnLobby is { } own)
            _footer.AddChild(Button("flow-my-lobby", () => ShowLobby(own)));
        else if (!state.InRound && _entities.System<ClientGameTicker>().AreWeReady)
            _footer.AddChild(FlowRow(Description("normal-ready"), Button("normal-unready", () =>
            {
                IoCManager.Resolve<IConsoleHost>().ExecuteCommand("toggleready false");
                Close();
            })));
    }

    private void JoinNormally()
    {
        if (_state is not { CanJoin: true, OwnLobby: null })
            return;
        if (_entities.System<ClientGameTicker>().IsGameStarted)
            OpenStandardJobs();
        else
        {
            IoCManager.Resolve<IConsoleHost>().ExecuteCommand("toggleready true");
            Close();
        }
    }

    /// <summary>Lists crews beside a single selected lobby's admission controls.</summary>
    private void BuildBrowser(WFRoundSetupState state)
    {
        var lobbies = state.Lobbies.Where(lobby => lobby.Id != state.OwnLobby)
            .OrderByDescending(lobby => state.Invitations.Any(invitation => invitation.Id == lobby.Id)).ToArray();
        var selected = lobbies.FirstOrDefault(lobby => lobby.Id == _focusedLobby) ?? lobbies.FirstOrDefault();
        _body.AddChild(Heading("browse"));
        if (selected == null)
        {
            _body.AddChild(Description("empty"));
            return;
        }
        _focusedLobby = selected.Id;
        if (_compact)
        {
            var picker = new OptionButton { HorizontalExpand = true, MaxWidth = ChoiceWidth, RectClipContent = true };
            foreach (var lobby in lobbies)
                picker.AddItem(lobby.HostName, lobby.Id);
            picker.SelectId(selected.Id);
            picker.OnItemSelected += args => ShowLobby(args.Id);
            _body.AddChild(picker);
        }
        else
        {
            _sidebarScroll.Visible = true;
            foreach (var lobby in lobbies)
            {
                var button = new Button { Text = lobby.HostName, ToggleMode = true, Pressed = lobby.Id == selected.Id, HorizontalExpand = true, MinHeight = 44 };
                button.OnPressed += _ => ShowLobby(lobby.Id);
                var listing = Column(6);
                listing.AddChild(button);
                listing.AddChild(PlainDescription(ScenarioName(lobby.Scenario)));
                _sidebar.AddChild(Panel(listing));
            }
        }
        _body.AddChild(LobbyCard(state, selected, state.Invitations.Any(invitation => invitation.Id == selected.Id)));
    }

    /// <summary>Creates the chosen start directly, without a second explanation or confirmation page.</summary>
    private void BuildScenarios(WFRoundSetupState state)
    {
        _body.AddChild(Heading("choose-scenario"));
        Container cards = _compact ? Column(12) : new ScenarioColumns { HorizontalExpand = true };
        foreach (var scenario in new[] { WFRoundScenario.Normal, WFRoundScenario.Crashlanded, WFRoundScenario.Ship })
        {
            var card = Column(16);
            card.MinHeight = 220;
            card.AddChild(PlainDescription(ScenarioName(scenario)));
            card.AddChild(Description(ScenarioDescription(scenario)));
            card.AddChild(new Control { VerticalExpand = true });
            var unavailable = scenario == WFRoundScenario.Crashlanded && !state.CrashlandedAvailable;
            if (unavailable)
                card.AddChild(Description("crash-unavailable"));
            var create = Button("create", () =>
            {
                _creatingLobby = true;
                SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = scenario });
            }, unavailable || !state.CanJoin);
            create.Name = "RoundSetupCreate" + scenario;
            create.MinHeight = 48;
            card.AddChild(create);
            cards.AddChild(Panel(card));
        }
        _body.AddChild(cards);
    }

    private void OpenStandardJobs()
    {
        new PickerWindow().OpenNormalJobs();
        Close();
    }

    /// <summary>Uses the same section order for navigation and the next action.</summary>
    private void BuildOwnLobby(WFRoundSetupState state, WFRoundSetupLobby lobby)
    {
        var user = _players.LocalSession?.UserId;
        var host = lobby.Host == user;
        var member = lobby.Members.FirstOrDefault(player => player.UserId == user);
        var pages = new List<(LobbyPage Page, string Label)> { (LobbyPage.Overview, "flow-overview") };
        if (lobby.Scenario == WFRoundScenario.Ship)
            pages.Add((LobbyPage.Ship, "ship"));
        if (host)
            pages.Add((LobbyPage.Admission, "admission"));
        pages.Add((LobbyPage.Roles, "roles"));
        pages.Add((LobbyPage.Character, "your-character"));
        if (!pages.Any(page => page.Page == _lobbyPage))
            _lobbyPage = LobbyPage.Overview;
        var sections = _compact ? Column(6) : Row();
        sections.Name = "RoundSetupSections";
        foreach (var (page, label) in pages)
        {
            var tab = NavigationButton(label, () => NavigateLobby(page), _lobbyPage == page);
            tab.HorizontalExpand = true;
            sections.AddChild(tab);
        }
        _body.AddChild(sections);
        switch (_lobbyPage)
        {
            case LobbyPage.Character:
                if (member is { Spawned: false } && state.CanJoin)
                    BuildMemberSetup(state, lobby, member);
                else
                    _body.AddChild(Description("already-playing"));
                break;
            case LobbyPage.Ship: BuildShipSettings(state, lobby, host); break;
            case LobbyPage.Roles:
                if (host)
                    BuildRoleEditor(state, lobby);
                else
                {
                    _body.AddChild(Heading("roles"));
                    foreach (var role in lobby.Roles)
                        _body.AddChild(Panel(PlainDescription(RoleChoice(role))));
                }
                break;
            case LobbyPage.Admission: BuildHostSettings(lobby, state); break;
            default:
                _body.AddChild(PlainDescription(Text("your-lobby", ("scenario", ScenarioName(lobby.Scenario)))));
                _body.AddChild(PlainDescription(Text("flow-current-access", ("access", AccessName(lobby.Access)))));
                BuildCrewRoster(lobby, host);
                break;
        }
        var index = pages.FindIndex(page => page.Page == _lobbyPage);
        if (index + 1 < pages.Count)
        {
            var next = pages[index + 1];
            var nextButton = new Button { Name = "RoundSetupNext", Text = Text("next-section", ("section", Text(next.Label))), MinHeight = 40 };
            nextButton.OnPressed += _ => NavigateLobby(next.Page);
            _footer.AddChild(nextButton);
        }
        BuildMemberFooter(state, lobby, member);
    }

    private void BuildCrewRoster(WFRoundSetupLobby lobby, bool host)
    {
        _body.AddChild(Heading("members"));
        foreach (var player in lobby.Members)
        {
            var status = !player.Connected ? "member-disconnected" : player.Spawned ? "member-playing" : player.Ready ? "member-ready" : "member-not-ready";
            var labels = Column(2);
            labels.HorizontalExpand = true;
            labels.AddChild(PlainDescription(player.Name));
            labels.AddChild(PlainDescription(Text("member-info", ("job", MemberRoleName(lobby, player)), ("status", Text(status)))));
            var row = Row(labels);
            if (host && player.UserId != lobby.Host)
            {
                row.AddChild(Button("transfer", () => SendTarget(WFRoundSetupAction.TransferHost, lobby.Id, player.UserId)));
                if (!player.Spawned)
                    row.AddChild(Button("kick", () => SendTarget(WFRoundSetupAction.Kick, lobby.Id, player.UserId)));
            }
            _body.AddChild(Panel(row));
        }
    }

    /// <summary>Keeps readiness and any purchase authorization visible without repeating setup instructions.</summary>
    private void BuildMemberFooter(WFRoundSetupState state, WFRoundSetupLobby lobby, WFRoundSetupMember? member)
    {
        if (member is not { Spawned: false } || !state.CanJoin)
        {
            _footer.AddChild(Button("leave", () => Send(WFRoundSetupAction.Leave, lobby.Id)));
            return;
        }
        var purchase = lobby.Scenario == WFRoundScenario.Ship && lobby.Host == member.UserId && !lobby.Started;
        var vessel = state.Vessels.FirstOrDefault(choice => choice.Id == lobby.Vessel);
        var eligible = member.RoleId == null
            ? state.Jobs.Any(job => job.Id == member.Job && (member.Station == null || job.Station == member.Station))
            : lobby.Roles.Any(role => role.Id == member.RoleId && role.CanSelect);
        var canPurchase = !purchase || vessel != null && state.HostBalance is { } balance && balance >= vessel.Price;
        var ready = new CheckBox
        {
            Name = "RoundSetupReady",
            Text = purchase && vessel != null ? Text("flow-ready-purchase-short", ("price", Price(vessel.Price))) : Text("ready"),
            Pressed = member.Ready,
            Disabled = !member.Ready && (!eligible || !canPurchase),
            HorizontalExpand = true,
        };
        ready.OnToggled += args => SetReady(lobby.Id, args.Pressed);
        var actions = FlowRow(ready);
        if (state.InRound && (lobby.Started || lobby.Host == member.UserId))
            actions.AddChild(Button(lobby.Started ? "join-round" : purchase ? "purchase-launch" : "launch",
                () => Send(WFRoundSetupAction.Launch, lobby.Id), !member.Ready || !eligible || !canPurchase));
        actions.AddChild(Button("leave", () => Send(WFRoundSetupAction.Leave, lobby.Id)));
        _footer.AddChild(Panel(actions));
    }
}
