using System.Linq;
using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._WG.RoundSetup;
using Content.Shared.GameTicking;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WG.RoundSetup;

/// <summary>Shows crew postings as a non-modal card for players who belong to no crew.</summary>
public sealed partial class WGCrewPostingCardSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private WGRoundSetupClientSystem _setup = default!;
    [Dependency] private WGCrewHudSystem _hud = default!;

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(3);

    private readonly List<(WGCrewPostingEvent Posting, TimeSpan Expires)> _postings = new();
    private PanelContainer _card = default!;
    private WGCrewPostingEvent? _shown;

    /// <summary>The posting currently on screen, if any.</summary>
    public WGCrewPostingEvent? Shown => _card.Visible ? _shown : null;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
        _card = new PanelContainer { Name = "CrewPostingCard", SetWidth = 320, Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#142630F2"), BorderColor = WGRecruitingGlow.Accent,
                BorderThickness = new Thickness(1), ContentMarginLeftOverride = 12, ContentMarginRightOverride = 12,
                ContentMarginTopOverride = 10, ContentMarginBottomOverride = 10,
            } };
        _ui.PopupRoot.AddChild(_card);
        LayoutContainer.SetAnchorPreset(_card, LayoutContainer.LayoutPreset.TopLeft);
        SubscribeNetworkEvent<WGCrewPostingEvent>(OnPosting);
        SubscribeNetworkEvent<WGCrewPostingRevokedEvent>(OnRevoked);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => Clear());
        _setup.Received += OnState;
        _net.Disconnect += OnDisconnect;
    }

    public override void Shutdown()
    {
        _setup.Received -= OnState;
        _net.Disconnect -= OnDisconnect;
        _card.Orphan();
        base.Shutdown();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (_postings.RemoveAll(entry => entry.Expires <= _timing.RealTime) > 0)
            Refresh();
        var width = _ui.PopupRoot.Width;
        _card.SetWidth = MathF.Min(320, MathF.Max(0, width - 24));
        var below = _hud.RequestVisible ? _hud.RequestHeight + 8 : 0;
        LayoutContainer.SetPosition(_card, new Vector2(MathF.Max(12, width - _card.SetWidth - 16),
            MathF.Max(12, _ui.PopupRoot.Height - _card.Height - 100 - below)));
    }

    private void OnPosting(WGCrewPostingEvent posting)
    {
        _postings.RemoveAll(entry => entry.Posting.LobbyId == posting.LobbyId);
        _postings.Add((posting, _timing.RealTime + Lifetime));
        Refresh();
    }

    private void OnRevoked(WGCrewPostingRevokedEvent args)
    {
        _postings.RemoveAll(entry => entry.Posting.LobbyId == args.LobbyId);
        Refresh();
    }

    // Joining any crew makes every open invitation moot; otherwise drop crews that stopped recruiting.
    private void OnState(WGRoundSetupState state)
    {
        if (_postings.Count == 0)
            return;
        var removed = state.OwnLobby != null
            ? _postings.RemoveAll(_ => true)
            : _postings.RemoveAll(entry => !state.Lobbies.Any(lobby => lobby.Id == entry.Posting.LobbyId && lobby.Recruiting));
        if (removed > 0)
            Refresh();
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args) => Clear();

    private void Clear()
    {
        _postings.Clear();
        Refresh();
    }

    private void Dismiss(int lobbyId)
    {
        _postings.RemoveAll(entry => entry.Posting.LobbyId == lobbyId);
        Refresh();
    }

    /// <summary>Shows the newest posting, rebuilding only when it changes.</summary>
    private void Refresh()
    {
        var next = _postings.Count == 0 ? null : _postings[^1].Posting;
        if (next == null)
        {
            _shown = null;
            _card.Visible = false;
            return;
        }
        if (ReferenceEquals(next, _shown) && _card.Visible)
            return;
        _shown = next;
        _card.RemoveAllChildren();
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        column.AddChild(new Label { Text = Loc.GetString("wg-round-setup-posting-card-title"), FontColorOverride = WGRecruitingGlow.Accent });
        var difficulty = WGRoundSetupPolicy.Difficulty(next.Scenario);
        column.AddChild(new Label { Text = Loc.GetString("wg-round-setup-posting-card-crew", ("crew", next.Crew),
            ("difficulty", WGScenarioDifficultyLabel.Name(difficulty)), ("openings", next.Openings)), ClipText = true });
        var message = new RichTextLabel();
        message.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("wg-round-setup-posting-card-message",
            ("host", next.Host), ("message", next.Message))));
        column.AddChild(message);
        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        var view = new Button { Text = Loc.GetString("wg-round-setup-posting-card-view"), HorizontalExpand = true,
            ToolTip = Loc.GetString(WGRoundSetupPolicy.TakesRequests(next.Access) ? "wg-round-setup-posting-card-request-help" : "wg-round-setup-posting-card-join-help") };
        view.AddStyleClass("ButtonAccept");
        var lobbyId = next.LobbyId;
        view.OnPressed += _ =>
        {
            _ui.GetUIController<WGRoundSetupUIController>().OpenLobby(lobbyId);
            Dismiss(lobbyId);
        };
        var dismiss = new Button { Text = Loc.GetString("wg-round-setup-posting-card-dismiss"), HorizontalExpand = true };
        dismiss.OnPressed += _ => Dismiss(lobbyId);
        var mute = new Button { Text = Loc.GetString("wg-round-setup-posting-card-mute"), HorizontalExpand = true,
            ToolTip = Loc.GetString("wg-round-setup-posting-card-mute-help") };
        mute.AddStyleClass(StyleBase.ButtonCaution);
        mute.OnPressed += _ =>
        {
            _config.SetCVar(WGRoundSetupCVars.ShowPostings, false);
            _config.SaveToFile();
            Clear();
        };
        buttons.AddChild(view);
        buttons.AddChild(dismiss);
        buttons.AddChild(mute);
        column.AddChild(buttons);
        _card.AddChild(column);
        _card.AddChild(new WGRecruitingGlow());
        _card.Visible = true;
    }
}
