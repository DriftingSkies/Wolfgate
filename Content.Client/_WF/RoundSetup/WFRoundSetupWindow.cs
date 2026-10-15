using System.Linq;
using System.Numerics;
using Content.Client.Lobby;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Roles;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.RoundSetup;

/// <summary>Browses starting scenarios and manages a player's group, job and admission settings.</summary>
public sealed partial class WFRoundSetupWindow : DefaultWindow
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IClientPreferencesManager _preferences = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly WFRoundSetupClientSystem _system;
    private readonly BoxContainer _body = Column(12);
    private readonly RichTextLabel _message = new() { HorizontalExpand = true, Visible = false };
    private readonly Dictionary<int, string> _enteredCodes = new();
    private WFRoundSetupState? _state;
    private Robust.Shared.Network.NetUserId? _inviteTarget;
    private int? _settingsLobby;
    private bool _settingsDirty;
    private WFLobbyAccess _draftAccess;
    private bool _draftLateJoin;
    private bool _refreshAfterRequest;

    /// <summary>Builds the browser and subscribes while its window is open.</summary>
    public WFRoundSetupWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Text("title");
        MinSize = new Vector2(480, 360);
        SetSize = new Vector2(1120, 780);
        _system = _entities.System<WFRoundSetupClientSystem>();

        InitializeLayout();

        _system.Received += Receive;
        _preferences.OnServerDataLoaded += PreferencesLoaded;
        OnClose += () =>
        {
            _system.Received -= Receive;
            _preferences.OnServerDataLoaded -= PreferencesLoaded;
        };
        if (_system.State is { } state)
            Receive(state);
        else
            _body.AddChild(Description("loading"));
        Send(WFRoundSetupAction.Refresh);
    }

    /// <summary>Keeps the setup browser within the viewport.</summary>
    public void FitTo(Vector2 viewport)
    {
        var available = Vector2.Max(Vector2.One, viewport - new Vector2(32));
        MinSize = Vector2.Min(new Vector2(480, 360), available);
        SetSize = Vector2.Min(new Vector2(1120, 780), available);
        _compact = SetSize.X < 820;
        _lastLayoutWidth = SetSize.X;
        _layoutRefreshPending = false;
        Redraw();
    }

    private void PreferencesLoaded()
    {
        if (_state == null)
            return;
        var state = _state;
        _refreshAfterRequest = true;
        Receive(state);
    }

    /// <summary>Updates the browser from the recipient-specific server snapshot.</summary>
    private void Receive(WFRoundSetupState state)
    {
        var previous = _state;
        _state = state;
        if (previous?.OwnLobby != state.OwnLobby)
        {
            _selectedVessel = null;
            _roleDrafts.Clear();
            _inviteTarget = null;
            _pageScroll.VScroll = 0;
            _editingRole = null;
            if (previous != null && state.OwnLobby != null)
            {
                _view = SetupView.MyLobby;
                _lobbyPage = LobbyPage.Overview;
            }
        }
        if (!string.IsNullOrEmpty(state.Message))
        {
            _message.SetMessage(FormattedMessage.FromUnformatted(state.Message));
            _message.ToolTip = state.Message;
            _message.Visible = true;
        }
        var refresh = _refreshAfterRequest;
        _refreshAfterRequest = false;
        if (!refresh && previous != null && SameState(previous, state))
            return;
        RenderState(state);
    }

    /// <summary>Displays a lobby with the admission action its host permits.</summary>
    private Control LobbyCard(WFRoundSetupState state, WFRoundSetupLobby lobby, bool invitation)
    {
        var content = Column(12);
        content.AddChild(PlainDescription(Text("lobby-title", ("host", lobby.HostName), ("scenario", ScenarioName(lobby.Scenario)))));
        content.AddChild(PlainDescription(Text("lobby-info", ("count", lobby.MemberCount), ("access", AccessName(lobby.Access)),
            ("status", Text(lobby.Started ? "status-started" : "status-waiting")))));

        content.AddChild(Description(ScenarioDescription(lobby.Scenario)));
        if (lobby.Scenario == WFRoundScenario.Ship)
            content.AddChild(VesselSummary(state, lobby));
        var available = state.CanJoin && state.OwnLobby == null && lobby.MemberCount < WFRoundSetupPolicy.MaximumMembers &&
                        (!lobby.Started || lobby.AllowLateJoin);
        available &= BuildBrowseRoles(content, lobby, available);
        if (invitation)
        {
            content.AddChild(FlowRow(Button("accept-invite", () => JoinLobby(WFRoundSetupAction.AcceptInvite, lobby.Id), !available),
                Button("decline", () => Send(WFRoundSetupAction.DeclineInvite, lobby.Id))));
        }
        else if (lobby.Access == WFLobbyAccess.Code)
        {
            var code = new LineEdit
            {
                PlaceHolder = Text("code-placeholder"),
                Text = _enteredCodes.GetValueOrDefault(lobby.Id, string.Empty),
                HorizontalExpand = true,
                Editable = available,
            };
            code.OnTextChanged += _ => _enteredCodes[lobby.Id] = code.Text;
            content.AddChild(FlowRow(code, Button("join-code", () => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.Join)
            {
                LobbyId = lobby.Id,
                Code = code.Text.Trim(),
                RoleId = _browsedRoles.GetValueOrDefault(lobby.Id),
            }), !available)));
        }
        else if (lobby.Access is WFLobbyAccess.Public or WFLobbyAccess.Requests)
        {
            content.AddChild(Button(lobby.Access == WFLobbyAccess.Requests ? "request-join" : "join",
                () => JoinLobby(WFRoundSetupAction.Join, lobby.Id), !available));
        }
        else
            content.AddChild(Description(lobby.Access == WFLobbyAccess.Invite ? "invite-only" : "closed-help"));

        if (lobby.Started && !lobby.AllowLateJoin)
            content.AddChild(Description("latejoin-closed"));
        return Panel(content);
    }

    /// <summary>Offers admission settings, join approvals and direct invitations to the host.</summary>
    private void BuildHostSettings(WFRoundSetupLobby lobby, WFRoundSetupState state)
    {
        if (_settingsLobby != lobby.Id || !_settingsDirty)
        {
            _settingsLobby = lobby.Id;
            _settingsDirty = false;
            _draftAccess = lobby.Access;
            _draftLateJoin = lobby.AllowLateJoin;
        }
        var settings = Column(6);
        settings.AddChild(Heading("admission"));
        var access = new WFRoundSetupOptionButton { HorizontalExpand = true };
        foreach (var option in Enum.GetValues<WFLobbyAccess>())
            access.AddItem(AccessName(option), (int) option);
        access.SelectId((int) _draftAccess);
        access.OnItemSelected += args =>
        {
            access.SelectId(args.Id);
            _draftAccess = (WFLobbyAccess) args.Id;
            _settingsDirty = true;
        };
        var lateJoin = new CheckBox { Text = Text("allow-latejoin"), Pressed = _draftLateJoin };
        lateJoin.OnToggled += args =>
        {
            _draftLateJoin = args.Pressed;
            _settingsDirty = true;
        };
        settings.AddChild(Description("admission-help"));
        settings.AddChild(access);
        settings.AddChild(lateJoin);

        settings.AddChild(Button("save-settings", () =>
        {
            _settingsDirty = false;
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.Configure)
            {
                LobbyId = lobby.Id,
                Access = _draftAccess,
                AllowLateJoin = _draftLateJoin,
            });
        }));
        if (lobby.Code != null)
        {
            var code = new LineEdit { Text = lobby.Code, Editable = false, HorizontalExpand = true };
            settings.AddChild(Heading("share-code"));
            settings.AddChild(FlowRow(code,
                Button("rotate-code", () => Send(WFRoundSetupAction.RegenerateCode, lobby.Id))));
        }
        _body.AddChild(Panel(settings));

        if (lobby.Requests.Length > 0)
        {
            _body.AddChild(Heading("requests"));
            foreach (var request in lobby.Requests)
            {
                var role = lobby.Roles.FirstOrDefault(choice => choice.Id == request.RoleId);
                var label = Text("request-role", ("name", request.Name),
                    ("role", role == null ? Text("job-unselected") : RoleName(role)));
                _body.AddChild(PlainDescription(label));
                _body.AddChild(FlowRow(
                    Button("accept", () => SendTarget(WFRoundSetupAction.AcceptRequest, lobby.Id, request.UserId)),
                    Button("reject", () => SendTarget(WFRoundSetupAction.RejectRequest, lobby.Id, request.UserId))));
            }
        }
        if (state.InviteTargets.Length == 0)
            return;

        var target = new WFRoundSetupOptionButton { HorizontalExpand = true };
        for (var i = 0; i < state.InviteTargets.Length; i++)
            target.AddItem(state.InviteTargets[i].Name, i);
        target.SelectId(Math.Max(0, Array.FindIndex(state.InviteTargets, player => player.UserId == _inviteTarget)));
        target.OnItemSelected += args =>
        {
            target.SelectId(args.Id);
            _inviteTarget = state.InviteTargets[args.Id].UserId;
        };
        _body.AddChild(Heading("invite"));
        _body.AddChild(FlowRow(target, Button("invite", () => SendTarget(WFRoundSetupAction.Invite,
            lobby.Id, state.InviteTargets[target.SelectedId].UserId))));
    }

    /// <summary>Lets an unspawned member choose their own character, job and readiness.</summary>
    private void BuildMemberSetup(WFRoundSetupState state, WFRoundSetupLobby lobby, WFRoundSetupMember member)
    {
        _body.AddChild(Heading("your-character"));
        if (_preferences.Preferences is { } preferences)
        {
            var characters = new WFRoundSetupOptionButton { HorizontalExpand = true, Disabled = member.Ready, ToolTip = Text("unready-edit") };
            foreach (var (slot, profile) in preferences.Characters.OrderBy(pair => pair.Key))
                characters.AddItem(profile.Name, slot);
            characters.SelectId(preferences.SelectedCharacterIndex);
            characters.OnItemSelected += args =>
            {
                characters.SelectId(args.Id);
                _preferences.SelectCharacter(args.Id);
                SetReady(lobby.Id, false);
                UserInterfaceManager.GetUIController<LobbyUIController>().ReloadCharacterSetup();
            };
            _body.AddChild(characters);
        }

        if (member.RoleId == null)
            BuildPersonalJobSetup(state, lobby, member);
        else
            BuildMemberRoles(lobby, member);

    }

    /// <summary>Keeps unchanged periodic snapshots from interrupting text entry and open selectors.</summary>
    private static bool SameState(WFRoundSetupState first, WFRoundSetupState second)
    {
        return first.InRound == second.InRound && first.CanJoin == second.CanJoin && first.OwnLobby == second.OwnLobby &&
               first.CrashlandedAvailable == second.CrashlandedAvailable && first.HostBalance == second.HostBalance &&
               first.Vessels.SequenceEqual(second.Vessels) &&
               first.Jobs.SequenceEqual(second.Jobs) && first.InviteTargets.SequenceEqual(second.InviteTargets) &&
               first.Lobbies.Length == second.Lobbies.Length && first.Invitations.Length == second.Invitations.Length &&
               first.Lobbies.Zip(second.Lobbies).All(pair => SameLobby(pair.First, pair.Second)) &&
               first.Invitations.Zip(second.Invitations).All(pair => SameLobby(pair.First, pair.Second));
    }

    private static bool SameLobby(WFRoundSetupLobby first, WFRoundSetupLobby second)
    {
        return first.Id == second.Id && first.Host == second.Host && first.HostName == second.HostName &&
               first.Scenario == second.Scenario && first.Access == second.Access && first.AllowLateJoin == second.AllowLateJoin &&
               first.Started == second.Started && first.MemberCount == second.MemberCount && first.Code == second.Code &&
               first.Vessel == second.Vessel && first.Roles.SequenceEqual(second.Roles) &&
               first.Members.SequenceEqual(second.Members) && first.Requests.SequenceEqual(second.Requests);
    }

    private void SendRequest(WFRoundSetupRequest request)
    {
        _message.Visible = false;
        _refreshAfterRequest |= request.Action != WFRoundSetupAction.Refresh;
        _system.Send(request);
    }

    private void SetReady(int lobby, bool ready)
    {
        SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = lobby, Ready = ready });
    }

    private void Send(WFRoundSetupAction action, int lobby = 0)
    {
        SendRequest(new WFRoundSetupRequest(action) { LobbyId = lobby });
    }

    private void SendTarget(WFRoundSetupAction action, int lobby, Robust.Shared.Network.NetUserId target)
    {
        SendRequest(new WFRoundSetupRequest(action) { LobbyId = lobby, Target = target });
    }

    private string JobName(string? id)
    {
        if (id == "Contractor")
            return Text("job-drifter");
        return id != null && _prototypes.TryIndex<JobPrototype>(id, out var prototype)
            ? prototype.LocalizedName : Text("job-unselected");
    }

    private static string ScenarioName(WFRoundScenario scenario) => Text(scenario switch
    {
        WFRoundScenario.Crashlanded => "scenario-crash",
        WFRoundScenario.Ship => "scenario-ship",
        _ => "scenario-normal",
    });

    private static string ScenarioDescription(WFRoundScenario scenario) => scenario switch
    {
        WFRoundScenario.Crashlanded => "crash-description",
        WFRoundScenario.Ship => "ship-description",
        _ => "normal-description",
    };

    private static string AccessName(WFLobbyAccess access) => Text($"access-{access.ToString().ToLowerInvariant()}");

    private static string Text(string key, params (string, object)[] args) => Loc.GetString($"wf-round-setup-{key}", args);

    private static BoxContainer Column(int separation) => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = separation, HorizontalExpand = true };

    private static BoxContainer Row(params Control[] controls)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8, HorizontalExpand = true };
        foreach (var control in controls)
            row.AddChild(control);
        return row;
    }

    private static RichTextLabel Heading(string key, params (string, object)[] args)
    {
        var heading = PlainDescription(Text(key, args));
        heading.StyleClasses.Add("LabelHeading");
        return heading;
    }

    private static RichTextLabel Description(string key) => PlainDescription(Text(key));

    private static RichTextLabel PlainDescription(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromUnformatted(text));
        return label;
    }

    private static Button Button(string key, Action pressed, bool disabled = false)
    {
        var button = new Button { Text = Text(key), Disabled = disabled, MinHeight = 40, HorizontalExpand = true };
        button.Label.Visible = false;
        var caption = PlainDescription(button.Text);
        caption.MouseFilter = MouseFilterMode.Ignore;
        button.AddChild(caption);
        button.OnPressed += _ => pressed();
        return button;
    }

    private static PanelContainer Panel(Control content)
    {
        content.Margin = new Thickness(14);
        return new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#19242C"),
                BorderColor = Color.FromHex("#3C505B"),
                BorderThickness = new Thickness(1),
            },
            Children = { content },
        };
    }
}
