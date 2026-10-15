using System.Linq;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.RoundSetup;

/// <summary>Shows live crews in a dedicated, scrollable lobby panel.</summary>
public sealed partial class WFRoundSetupLobbyPanel : PanelContainer
{
    [Dependency] private IEntityManager _entities = default!;

    private WFRoundSetupClientSystem? _system;
    private readonly BoxContainer _cards;
    private readonly ScrollContainer _scroll;
    private readonly RichTextLabel _empty;
    private Listing[] _shown = Array.Empty<Listing>();
    private bool _listening;

    /// <summary>Stores only the public fields displayed on a lobby card.</summary>
    private sealed record Listing(int Id, string Host, WFRoundScenario Scenario, WFLobbyAccess Access,
        int Members, int Openings, bool Started, bool AllowLateJoin, bool Own, bool Invited);

    /// <summary>Builds a live crew list with wrapped identities and available openings.</summary>
    public WFRoundSetupLobbyPanel()
    {
        IoCManager.InjectDependencies(this);
        HorizontalExpand = true;
        MinHeight = 96;
        StyleClasses.Add("WFRoundSetupInset");
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(8),
        };
        AddChild(content);
        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        header.AddChild(Wrapped(Text("title")));
        var refresh = new Button { Text = Text("refresh"), ToolTip = Text("refresh-help") };
        refresh.OnPressed += _ => Refresh();
        header.AddChild(refresh);
        content.AddChild(header);

        _empty = Wrapped(Text("loading"));
        _empty.ToolTip = Text("empty-help");
        content.AddChild(_empty);
        _cards = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
        };
        _scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            VScrollEnabled = true,
            Visible = false,
            Children = { _cards },
        };
        content.AddChild(_scroll);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        SetListening(VisibleInTree);
    }

    protected override void ExitedTree()
    {
        SetListening(false);
        base.ExitedTree();
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        SetListening(VisibleInTree);
    }

    /// <summary>Follows snapshots only while this screen is visible, including after character setup closes.</summary>
    private void SetListening(bool listening)
    {
        if (_listening == listening)
            return;
        if (!listening)
        {
            if (_system != null)
                _system.Received -= Receive;
            _system = null;
            _listening = false;
            return;
        }
        _system = _entities.System<WFRoundSetupClientSystem>();
        _system.Received += Receive;
        _listening = true;
        if (_system.State is { } state)
            Receive(state);
        Refresh();
    }

    /// <summary>Registers this viewer for the server's live lobby snapshots.</summary>
    private void Refresh() => _system?.Send(new WFRoundSetupRequest(WFRoundSetupAction.Refresh));

    /// <summary>Updates public cards without rebuilding unchanged snapshots or disturbing scrolling.</summary>
    private void Receive(WFRoundSetupState state)
    {
        var listings = state.Lobbies
            .OrderByDescending(lobby => lobby.Id == state.OwnLobby)
            .ThenBy(lobby => lobby.Started)
            .ThenBy(lobby => lobby.Id)
            .Select(lobby => new Listing(lobby.Id, lobby.HostName, lobby.Scenario, lobby.Access,
                lobby.MemberCount, lobby.Roles.Sum(role => Math.Max(0, role.Capacity - role.Occupied)),
                lobby.Started, lobby.AllowLateJoin, lobby.Id == state.OwnLobby,
                state.Invitations.Any(invitation => invitation.Id == lobby.Id)))
            .ToArray();
        _empty.SetMessage(FormattedMessage.FromUnformatted(Text("empty")));
        _empty.Visible = listings.Length == 0;
        _scroll.Visible = listings.Length != 0;
        if (_shown.SequenceEqual(listings))
            return;
        _shown = listings;
        _cards.DisposeAllChildren();
        foreach (var listing in listings)
            _cards.AddChild(LobbyCard(listing));
    }

    /// <summary>Opens a lobby's details without sending an admission or role selection request.</summary>
    private Button LobbyCard(Listing listing)
    {
        var host = Text("host", ("host", listing.Host));
        var scenario = Loc.GetString(listing.Scenario switch
        {
            WFRoundScenario.Ship => "wf-round-setup-scenario-ship",
            WFRoundScenario.Crashlanded => "wf-round-setup-scenario-crash",
            _ => "wf-round-setup-scenario-normal",
        });
        var access = Text($"access-{listing.Access.ToString().ToLowerInvariant()}");
        var status = Text(listing.Started ? listing.AllowLateJoin ? "started-open" : "started-closed" : "waiting");
        if (listing.Own)
            status = Text("own-status", ("status", status));
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            MouseFilter = MouseFilterMode.Ignore,
        };
        var identity = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        };
        foreach (var text in new[] { scenario, host })
        {
            var label = new RichTextLabel { HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
            if (text == scenario)
                label.StyleClasses.Add("WFRoundSetupHeading");
            label.SetMessage(FormattedMessage.FromUnformatted(text));
            identity.AddChild(label);
        }
        content.AddChild(identity);
        var details = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        details.AddChild(Wrapped(Text("members", ("count", listing.Members), ("access", access))));
        details.AddChild(Wrapped(Text("openings", ("count", listing.Openings))));
        details.AddChild(Wrapped(status));
        if (listing.Invited)
            details.AddChild(Wrapped(Text("invited")));
        content.AddChild(details);
        var button = new Button
        {
            HorizontalExpand = true,
            ToolTip = Text("open-help", ("host", listing.Host)),
            Children = { content },
        };
        button.Label.Visible = false;
        button.OnPressed += _ => UserInterfaceManager.GetUIController<WFRoundSetupUIController>().OpenLobby(listing.Id);
        return button;
    }

    private static RichTextLabel Wrapped(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        label.SetMessage(FormattedMessage.FromUnformatted(text));
        return label;
    }

    private static string Text(string key, params (string, object)[] args) => Loc.GetString($"wf-round-setup-panel-{key}", args);
}
