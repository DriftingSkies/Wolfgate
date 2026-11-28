using System.Numerics;
using Content.Shared._WG.Settlers;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._WG.Settlers;

/// <summary>
/// The Settlers of Colossus window: the board on the left; the seats, the local player's hand, moves, contracts,
/// trades and the game log on the right.
/// </summary>
public sealed class SettlersWindow : DefaultWindow
{
    private static readonly Color TurnColor = Color.FromHex("#E8C020");
    private static readonly Color CardColor = Color.FromHex("#1A1C24");
    private static readonly Color CardEdge = Color.FromHex("#2E3240");
    private static readonly Color BannerColor = Color.FromHex("#1C1F2A");
    private static readonly Color BadgeRoute = Color.FromHex("#C08A2E");
    private static readonly Color BadgeFleet = Color.FromHex("#3E7CB1");
    private static readonly Color BadgeDiscard = Color.FromHex("#B3261E");
    private static readonly ResolvedSoundSpecifier DiceSound = new ResolvedPathSpecifier("/Audio/Items/Dice/dice3.ogg");

    private readonly IEntityManager _entMan;
    private readonly IPlayerManager _player;
    private readonly IConfigurationManager _cfg;

    private readonly SettlersBoardControl _board = new();

    private readonly PanelContainer[] _seatCards = new PanelContainer[SettlersRules.MaxPlayers];
    private readonly StyleBoxFlat[] _seatStyles = new StyleBoxFlat[SettlersRules.MaxPlayers];
    private readonly Label[] _seatNames = new Label[SettlersRules.MaxPlayers];
    private readonly Label[] _seatStats = new Label[SettlersRules.MaxPlayers];
    private readonly Label[] _seatPoints = new Label[SettlersRules.MaxPlayers];
    private readonly BoxContainer[] _seatBadges = new BoxContainer[SettlersRules.MaxPlayers];
    private readonly Button[] _seatButtons = new Button[SettlersRules.MaxPlayers];

    private readonly StyleBoxFlat _bannerStyle = SettlersArt.Box(BannerColor, TurnColor, 0f, 10f, 6f);
    private readonly Label _status = new();
    private readonly Label _hint = new();
    private readonly Button _start = new() { Text = Loc.GetString("wg-settlers-start") };
    private readonly Button _newGame = new() { Text = Loc.GetString("wg-settlers-new-game") };

    private readonly BoxContainer _playArea = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
    private readonly Label _points = new();
    private readonly Label _deck = new();
    private readonly SettlersHandRow _hand = new();

    private readonly Button _roll = new() { Text = Loc.GetString("wg-settlers-roll"), HorizontalExpand = true, MinHeight = 30f };
    private readonly Button _endTurn = new() { Text = Loc.GetString("wg-settlers-end-turn"), HorizontalExpand = true, MinHeight = 30f };
    private readonly Button _endSurvey = new() { Text = Loc.GetString("wg-settlers-end-survey"), HorizontalExpand = true, MinHeight = 30f };
    private readonly SettlersCostButton _buildLane = new(Loc.GetString("wg-settlers-build-lane"), SettlersRules.LaneCost) { ToggleMode = true };
    private readonly SettlersCostButton _buildOutpost = new(Loc.GetString("wg-settlers-build-outpost"), SettlersRules.OutpostCost) { ToggleMode = true };
    private readonly SettlersCostButton _buildColony = new(Loc.GetString("wg-settlers-build-colony"), SettlersRules.ColonyCost) { ToggleMode = true };
    private readonly SettlersCostButton _buyCard = new(Loc.GetString("wg-settlers-buy-card"), SettlersRules.CardCost);

    private readonly GridContainer _cards = new() { Columns = 2, HorizontalExpand = true };
    private readonly BoxContainer _choice = new() { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
    private readonly Label _choiceLabel = new();

    private readonly BoxContainer _steal = new() { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };

    private readonly BoxContainer _discardBox = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
    private readonly Label _discardLabel = new();
    private readonly SettlersCounterRow _discard = new();
    private readonly Button _discardButton = new() { Text = Loc.GetString("wg-settlers-discard") };

    private readonly OptionButton _bankGive = new() { HorizontalExpand = true };
    private readonly OptionButton _bankGet = new() { HorizontalExpand = true };
    private readonly Button _bankTrade = new();
    private readonly SettlersCounterRow _offerGive = new(Loc.GetString("wg-settlers-offer-give"));
    private readonly SettlersCounterRow _offerWant = new(Loc.GetString("wg-settlers-offer-want"));
    private readonly Button _postOffer = new() { Text = Loc.GetString("wg-settlers-post-offer"), HorizontalExpand = true };
    private readonly Button _withdrawOffer = new() { Text = Loc.GetString("wg-settlers-withdraw-offer"), HorizontalExpand = true };
    private readonly BoxContainer _offers = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };

    private readonly BoxContainer _log = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 1 };

    private SettlersStateMessage? _message;
    private int? _seat;
    private SettlersPick _buildPick;

    /// <summary>A windfall or embargo waiting for the resources it names, and the windfall's first pick.</summary>
    private SettlersCard? _pendingCard;
    private int? _firstPick;
    private int _rollCount = -1;

    public event Action<int>? OnSeat;
    public event Action? OnStart;
    public event Action? OnNewGame;
    public event Action<SettlersAction>? OnAction;

    public SettlersWindow()
    {
        _entMan = IoCManager.Resolve<IEntityManager>();
        _player = IoCManager.Resolve<IPlayerManager>();
        _cfg = IoCManager.Resolve<IConfigurationManager>();

        Title = Loc.GetString("wg-settlers-window-title");
        SetSize = new Vector2(1120f, 760f);
        MinSize = new Vector2(980f, 620f);

        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            HorizontalExpand = true,
            Margin = new Thickness(0f, 0f, 6f, 0f),
        };

        var seats = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        for (var seat = 0; seat < SettlersRules.MaxPlayers; seat++)
        {
            seats.AddChild(MakeSeat(seat));
        }

        side.AddChild(seats);
        side.AddChild(MakeBanner());

        BuildPlayArea();
        side.AddChild(_playArea);

        side.AddChild(Section("wg-settlers-section-log", null, _log));

        var scroll = new ScrollContainer { HScrollEnabled = false, MinWidth = 470f, VerticalExpand = true };
        scroll.AddChild(side);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 10 };
        root.AddChild(_board);
        root.AddChild(scroll);
        Contents.AddChild(root);

        _board.OnPicked += OnPicked;
        _start.OnPressed += _ => OnStart?.Invoke();
        _newGame.OnPressed += _ => OnNewGame?.Invoke();
    }

    private void BuildPlayArea()
    {
        _points.FontOverride = SettlersArt.Font(12);
        _points.FontColorOverride = SettlersArt.Heading;
        _playArea.AddChild(Section("wg-settlers-section-hand", _points, _hand));

        var turn = Column(4);
        turn.AddChild(Row(_roll, _endTurn, _endSurvey));
        var builds = new GridContainer { Columns = 2, HorizontalExpand = true, HSeparationOverride = 4, VSeparationOverride = 4 };
        builds.AddChild(_buildLane);
        builds.AddChild(_buildOutpost);
        builds.AddChild(_buildColony);
        builds.AddChild(_buyCard);
        turn.AddChild(builds);
        turn.AddChild(_steal);
        turn.AddChild(_discardBox);
        _playArea.AddChild(Section("wg-settlers-section-turn", null, turn));

        _roll.OnPressed += _ => Act(SettlersActionKind.Roll);
        _endTurn.OnPressed += _ => Act(SettlersActionKind.EndTurn);
        _endSurvey.OnPressed += _ => Act(SettlersActionKind.EndSurvey);
        _buyCard.OnPressed += _ => Act(SettlersActionKind.BuyCard);
        _buildLane.OnToggled += args => ToggleBuild(SettlersPick.Lane, args.Pressed);
        _buildOutpost.OnToggled += args => ToggleBuild(SettlersPick.Outpost, args.Pressed);
        _buildColony.OnToggled += args => ToggleBuild(SettlersPick.Colony, args.Pressed);

        _steal.Visible = false;

        _discardLabel.FontColorOverride = BadgeDiscard;
        _discardBox.AddChild(_discardLabel);
        _discardBox.AddChild(SettlersCounterRow.Header(false));
        _discardBox.AddChild(_discard);
        _discardBox.AddChild(_discardButton);
        _discard.OnChanged += Refresh;
        _discardButton.OnPressed += _ => SendCounts(SettlersActionKind.Discard, _discard.Counts, Array.Empty<int>());

        _choiceLabel.VerticalAlignment = VAlignment.Center;
        _choice.AddChild(_choiceLabel);
        for (var resource = 0; resource < SettlersRules.ResourceCount; resource++)
        {
            var index = resource;
            var button = new Button { ToolTip = SettlersArt.ResourceName(resource) };
            button.AddChild(new TextureRect
            {
                Texture = SettlersArt.ResourceTexture(resource),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
                MinSize = new Vector2(24f, 24f),
            });
            button.OnPressed += _ => PickResource(index);
            _choice.AddChild(button);
        }

        var cancel = new Button { Text = Loc.GetString("wg-settlers-cancel") };
        cancel.OnPressed += _ => CancelCard();
        _choice.AddChild(cancel);
        _choice.Visible = false;
        var contracts = Column(4);
        contracts.AddChild(_cards);
        contracts.AddChild(_choice);
        _deck.FontColorOverride = SettlersArt.Muted;
        _deck.StyleClasses.Add("LabelSubText");
        _playArea.AddChild(Section("wg-settlers-section-contracts", _deck, contracts));

        for (var resource = 0; resource < SettlersRules.ResourceCount; resource++)
        {
            _bankGive.AddItem(SettlersArt.ResourceName(resource), resource);
            _bankGet.AddItem(SettlersArt.ResourceName(resource), resource);
        }

        _bankGet.SelectId(1);
        _bankGive.OnItemSelected += args =>
        {
            _bankGive.SelectId(args.Id);
            Refresh();
        };
        _bankGet.OnItemSelected += args =>
        {
            _bankGet.SelectId(args.Id);
            Refresh();
        };
        _bankTrade.OnPressed += _ => OnAction?.Invoke(new SettlersAction(SettlersActionKind.BankTrade, 0, _bankGive.SelectedId, _bankGet.SelectedId));

        var trade = Column(4);
        trade.AddChild(Row(Muted("wg-settlers-bank-give", SettlersCounterRow.LabelWidth), _bankGive,
            Muted("wg-settlers-bank-get"), _bankGet, _bankTrade));
        trade.AddChild(Divider());
        trade.AddChild(SettlersCounterRow.Header(true));
        trade.AddChild(_offerGive);
        trade.AddChild(_offerWant);
        trade.AddChild(Row(_postOffer, _withdrawOffer));
        trade.AddChild(_offers);
        _playArea.AddChild(Section("wg-settlers-section-trade", null, trade));

        _offerGive.OnChanged += Refresh;
        _offerWant.OnChanged += Refresh;
        _postOffer.OnPressed += _ => SendCounts(SettlersActionKind.Offer, _offerGive.Counts, _offerWant.Counts);
        _withdrawOffer.OnPressed += _ => SendCounts(SettlersActionKind.Offer, new int[SettlersRules.ResourceCount], new int[SettlersRules.ResourceCount]);
    }

    /// <summary>A boxed section: a gold heading, something on its right, and its contents below.</summary>
    private static PanelContainer Section(string loc, Control? right, Control body)
    {
        var heading = new Label
        {
            Text = Loc.GetString(loc).ToUpperInvariant(),
            FontOverride = SettlersArt.Font(10),
            FontColorOverride = SettlersArt.Heading,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };

        var header = Row(heading);
        if (right != null)
            header.AddChild(right);

        var column = Column(6);
        column.AddChild(header);
        column.AddChild(body);

        var panel = new PanelContainer { PanelOverride = SettlersArt.Box(SettlersArt.PanelColor, SettlersArt.PanelEdge) };
        panel.AddChild(column);
        return panel;
    }

    private static BoxContainer Row(params Control[] children)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        foreach (var child in children)
        {
            row.AddChild(child);
        }

        return row;
    }

    private static BoxContainer Column(int separation)
    {
        return new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = separation };
    }

    private static Label Muted(string loc, float minWidth = 0f)
    {
        return new Label
        {
            Text = Loc.GetString(loc),
            MinWidth = minWidth,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = SettlersArt.Muted,
        };
    }

    private static PanelContainer Divider()
    {
        return new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = SettlersArt.PanelEdge },
            MinHeight = 1f,
            Margin = new Thickness(0f, 2f),
        };
    }

    /// <summary>Counts as a strip of icons with numbers, as on a table offer.</summary>
    private static BoxContainer Chips(int[] counts)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
        row.ToolTip = SettlersArt.Counts(counts);
        for (var resource = 0; resource < counts.Length; resource++)
        {
            if (counts[resource] == 0)
                continue;

            row.AddChild(new TextureRect
            {
                Texture = SettlersArt.ResourceTexture(resource),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
                MinSize = new Vector2(20f, 20f),
            });
            row.AddChild(new Label { Text = counts[resource].ToString(), VerticalAlignment = VAlignment.Center, Margin = new Thickness(0f, 0f, 4f, 0f) });
        }

        return row;
    }

    /// <summary>The status line under the seats, edged in the colour of the player whose turn it is.</summary>
    private PanelContainer MakeBanner()
    {
        _status.FontOverride = SettlersArt.Font(14);
        _hint.StyleClasses.Add("LabelSubText");
        _hint.FontColorOverride = SettlersArt.Muted;

        var buttons = Row(_start, _newGame);
        buttons.HorizontalAlignment = HAlignment.Right;

        var text = Column(2);
        text.AddChild(_status);
        text.AddChild(_hint);
        text.AddChild(buttons);

        var banner = new PanelContainer { PanelOverride = _bannerStyle };
        banner.AddChild(text);
        return banner;
    }

    /// <summary>A seat's card: the player's colour, name, public numbers and badges, points, and the button to join or leave.</summary>
    private PanelContainer MakeSeat(int seat)
    {
        var style = SettlersArt.Box(CardColor, CardEdge, 1f, 8f, 4f);
        _seatStyles[seat] = style;

        var swatch = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = SettlersArt.SeatColors[seat] },
            MinSize = new Vector2(6f, 32f),
            VerticalExpand = true,
        };

        _seatNames[seat] = new Label { ClipText = true, HorizontalExpand = true, FontOverride = SettlersArt.Font(12) };
        _seatStats[seat] = new Label { ClipText = true, FontColorOverride = SettlersArt.Muted };
        _seatStats[seat].StyleClasses.Add("LabelSubText");
        _seatBadges[seat] = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };

        var nameRow = Row(_seatNames[seat], _seatBadges[seat]);
        var text = Column(0);
        text.HorizontalExpand = true;
        text.AddChild(nameRow);
        text.AddChild(_seatStats[seat]);

        _seatPoints[seat] = new Label
        {
            FontOverride = SettlersArt.Font(20),
            VerticalAlignment = VAlignment.Center,
            MinWidth = 28f,
            Align = Label.AlignMode.Right,
            ToolTip = Loc.GetString("wg-settlers-points-tooltip"),
        };

        _seatButtons[seat] = new Button { VerticalAlignment = VAlignment.Center };
        _seatButtons[seat].OnPressed += _ => OnSeat?.Invoke(_seat == seat ? -1 : seat);

        var box = Row(swatch, text, _seatPoints[seat], _seatButtons[seat]);
        box.SeparationOverride = 8;
        var card = new PanelContainer { PanelOverride = style, HorizontalExpand = true };
        card.AddChild(box);
        _seatCards[seat] = card;
        return card;
    }

    private static PanelContainer Badge(string text, Color color)
    {
        var badge = new PanelContainer
        {
            PanelOverride = SettlersArt.Box(Color.InterpolateBetween(color, Color.Black, 0.55f), color, 1f, 4f, 0f),
            VerticalAlignment = VAlignment.Center,
        };
        var label = new Label { Text = text, FontColorOverride = Color.InterpolateBetween(color, Color.White, 0.5f) };
        label.StyleClasses.Add("LabelSubText");
        badge.AddChild(label);
        return badge;
    }

    public void UpdateState(SettlersStateMessage message)
    {
        _message = message;
        _seat = null;
        if (_player.LocalEntity is { } local && _entMan.TryGetNetEntity(local, out var net))
        {
            for (var seat = 0; seat < message.Seats.Length; seat++)
            {
                if (message.Seats[seat] == net)
                    _seat = seat;
            }
        }

        var state = message.State;
        if (_rollCount >= 0 && state.RollCount > _rollCount)
            PlayDice();

        _rollCount = state.RollCount;

        if (_seat is not { } mine || state.Phase != SettlersPhase.Discard || state.Discard[mine] == 0)
            _discard.Clear();

        if (state.Phase is not (SettlersPhase.Roll or SettlersPhase.Main) || _seat != state.Turn)
            CancelCard();

        Refresh();
    }

    private void PlayDice()
    {
        var gain = _cfg.GetCVar(CCVars.InterfaceVolume);
        if (gain <= 0f)
            return;

        _entMan.System<AudioSystem>().PlayGlobal(DiceSound, Filter.Local(), false,
            AudioParams.Default.WithVolume(SharedAudioSystem.GainToVolume(gain)));
    }

    private string SeatName(int seat)
    {
        if (_message?.SeatNames is { } names && seat < names.Length && names[seat] is { } name)
            return name;

        return Loc.GetString("wg-settlers-seat", ("number", seat + 1));
    }

    private void Refresh()
    {
        if (_message is not { } message)
            return;

        var state = message.State;
        var mine = _seat;
        var myTurn = mine != null && mine == state.Turn && state.Running;

        RefreshSeats(state, message);
        RefreshStatus(state, mine, myTurn);

        _start.Visible = state.Phase == SettlersPhase.Lobby;
        _start.Disabled = mine == null;
        _newGame.Visible = state.Phase != SettlersPhase.Lobby;
        _newGame.Disabled = mine == null || state.Running && SeatedCount(message) > 1;

        _playArea.Visible = mine != null && state.Players[mine.Value].InGame;
        if (mine is not { } seat || !_playArea.Visible)
        {
            _board.State = state;
            _board.Seat = null;
            _board.Pick = SettlersPick.None;
            RefreshLog(state);
            return;
        }

        var player = state.Players[seat];
        var main = myTurn && state.Phase == SettlersPhase.Main;
        _hand.Show(player.Hand);
        var hidden = player.HiddenPoints;
        _points.Text = hidden > 0
            ? Loc.GetString("wg-settlers-points-hidden", ("points", state.Points(seat)), ("hidden", hidden))
            : Loc.GetString("wg-settlers-points", ("points", state.Points(seat)));

        _roll.Disabled = !(myTurn && state.Phase == SettlersPhase.Roll);
        _endTurn.Disabled = !main;
        _endSurvey.Visible = myTurn && state.Phase == SettlersPhase.Survey;

        SetBuild(_buildLane, SettlersPick.Lane, main && state.CanAfford(seat, SettlersRules.LaneCost) && state.Lanes(seat) < SettlersRules.MaxLanes);
        SetBuild(_buildOutpost, SettlersPick.Outpost, main && state.CanAfford(seat, SettlersRules.OutpostCost) && state.Count(seat, false) < SettlersRules.MaxOutposts);
        SetBuild(_buildColony, SettlersPick.Colony, main && state.CanAfford(seat, SettlersRules.ColonyCost) && state.Count(seat, true) < SettlersRules.MaxColonies);
        _buyCard.Disabled = !main || state.DeckCount == 0 || !state.CanAfford(seat, SettlersRules.CardCost);
        _deck.Text = Loc.GetString("wg-settlers-deck", ("count", state.DeckCount));

        _board.State = state;
        _board.Seat = seat;
        _board.Pick = myTurn ? state.Phase switch
        {
            SettlersPhase.SetupOutpost => SettlersPick.Outpost,
            SettlersPhase.SetupLane or SettlersPhase.Survey => SettlersPick.Lane,
            SettlersPhase.MovePirates => SettlersPick.Pirates,
            SettlersPhase.Main => _buildPick,
            _ => SettlersPick.None,
        } : SettlersPick.None;

        RefreshCards(state, seat, myTurn);
        RefreshSteal(state, seat, myTurn);
        RefreshDiscard(state, seat);
        RefreshTrade(state, seat, main);
        RefreshLog(state);
    }

    private static int SeatedCount(SettlersStateMessage message)
    {
        var count = 0;
        foreach (var seat in message.Seats)
        {
            if (seat != null)
                count++;
        }

        return count;
    }

    private void RefreshSeats(SettlersState state, SettlersStateMessage message)
    {
        for (var seat = 0; seat < SettlersRules.MaxPlayers; seat++)
        {
            var player = state.Players[seat];
            var taken = seat < message.Seats.Length && message.Seats[seat] != null;
            var lit = state.Phase == SettlersPhase.Finished ? state.Winner == seat : state.Running && state.Turn == seat;
            _seatCards[seat].Visible = state.Phase == SettlersPhase.Lobby || player.InGame;
            _seatStyles[seat].BorderColor = lit ? TurnColor : CardEdge;
            _seatStyles[seat].BackgroundColor = lit ? Color.InterpolateBetween(CardColor, TurnColor, 0.08f) : CardColor;
            _seatNames[seat].FontColorOverride = lit ? TurnColor : taken ? null : SettlersArt.Muted;

            if (taken)
                _seatNames[seat].Text = SeatName(seat);
            else
                _seatNames[seat].Text = Loc.GetString(player.InGame ? "wg-settlers-seat-vacant" : "wg-settlers-seat-open", ("number", seat + 1));

            _seatBadges[seat].RemoveAllChildren();
            if (player.InGame)
            {
                _seatPoints[seat].Text = (seat == _seat ? state.Points(seat) : state.PublicPoints(seat)).ToString();
                _seatPoints[seat].FontColorOverride = lit ? TurnColor : null;
                _seatStats[seat].Text = Loc.GetString("wg-settlers-seat-stats", ("cards", state.HandSize(seat)),
                    ("contracts", player.CardCount), ("escorts", player.Escorts), ("route", player.Route));

                if (state.RouteHolder == seat)
                    _seatBadges[seat].AddChild(Badge(Loc.GetString("wg-settlers-badge-route"), BadgeRoute));
                if (state.FleetHolder == seat)
                    _seatBadges[seat].AddChild(Badge(Loc.GetString("wg-settlers-badge-fleet"), BadgeFleet));
                if (state.Discard[seat] > 0)
                    _seatBadges[seat].AddChild(Badge(Loc.GetString("wg-settlers-badge-discarding", ("count", state.Discard[seat])), BadgeDiscard));
            }
            else
            {
                _seatPoints[seat].Text = string.Empty;
                _seatStats[seat].Text = Loc.GetString(state.Running ? "wg-settlers-seat-not-playing" : "wg-settlers-seat-waiting");
            }

            var canJoin = _seat == null && !taken && (!state.Running || player.InGame) && state.Phase != SettlersPhase.Finished;
            _seatButtons[seat].Text = Loc.GetString(_seat == seat ? "wg-settlers-leave" : "wg-settlers-join");
            _seatButtons[seat].Visible = _seat == seat || canJoin;
        }
    }

    private void RefreshStatus(SettlersState state, int? mine, bool myTurn)
    {
        var turnName = SeatName(state.Turn);
        _status.Text = state.Phase switch
        {
            SettlersPhase.Lobby => Loc.GetString("wg-settlers-status-lobby"),
            SettlersPhase.Finished => Loc.GetString("wg-settlers-status-won", ("player", SeatName(state.Winner))),
            _ => Loc.GetString($"wg-settlers-status-{state.Phase.ToString().ToLowerInvariant()}", ("player", turnName), ("count", state.FreeLanes)),
        };

        string hint;
        if (mine == null)
            hint = Loc.GetString("wg-settlers-hint-spectator");
        else if (state.Phase == SettlersPhase.Lobby)
            hint = Loc.GetString("wg-settlers-hint-lobby", ("min", SettlersRules.MinPlayers), ("max", SettlersRules.MaxPlayers));
        else if (state.Phase == SettlersPhase.Finished)
            hint = Loc.GetString("wg-settlers-hint-finished");
        else if (state.Phase == SettlersPhase.Discard && state.Discard[mine.Value] > 0)
            hint = Loc.GetString("wg-settlers-hint-discard", ("count", state.Discard[mine.Value]));
        else if (state.Phase == SettlersPhase.Discard)
            hint = Loc.GetString("wg-settlers-hint-others-discarding");
        else if (!myTurn)
            hint = Loc.GetString("wg-settlers-hint-waiting", ("player", turnName));
        else
            hint = Loc.GetString($"wg-settlers-hint-{state.Phase.ToString().ToLowerInvariant()}", ("count", state.FreeLanes));

        _hint.Text = hint;

        int? edge = state.Phase switch
        {
            SettlersPhase.Lobby => null,
            SettlersPhase.Finished => state.Winner,
            _ => state.Turn,
        };

        _bannerStyle.BorderColor = edge is { } lit ? SettlersArt.SeatColors[lit] : SettlersArt.PanelEdge;
        _bannerStyle.BorderThickness = edge != null ? new Thickness(4f, 0f, 0f, 0f) : new Thickness(1f);
        _status.FontColorOverride = myTurn || state.Phase == SettlersPhase.Finished ? TurnColor : null;
    }

    private void SetBuild(BaseButton button, SettlersPick pick, bool enabled)
    {
        button.Disabled = !enabled;
        if (!enabled && _buildPick == pick)
            _buildPick = SettlersPick.None;

        button.Pressed = _buildPick == pick;
    }

    private void ToggleBuild(SettlersPick pick, bool pressed)
    {
        _buildPick = pressed ? pick : SettlersPick.None;
        Refresh();
    }

    private void RefreshCards(SettlersState state, int seat, bool myTurn)
    {
        _cards.RemoveAllChildren();
        var player = state.Players[seat];
        var canPlay = myTurn && state.Phase is SettlersPhase.Roll or SettlersPhase.Main && !state.PlayedCard;

        for (var card = 0; card < SettlersRules.CardCounts.Length; card++)
        {
            var kind = (SettlersCard) card;
            var held = Count(player.Cards, kind);
            var fresh = Count(player.NewCards, kind);
            if (held + fresh == 0)
                continue;

            var button = new Button
            {
                Text = Loc.GetString("wg-settlers-card-button", ("card", SettlersArt.CardName(kind)), ("count", held + fresh)),
                ToolTip = Loc.GetString($"wg-settlers-card-{kind.ToString().ToLowerInvariant()}-desc"),
                HorizontalExpand = true,
                Disabled = kind == SettlersCard.Charter || held == 0 || !canPlay,
            };

            if (kind != SettlersCard.Charter && held == 0)
                button.ToolTip += "\n" + Loc.GetString("wg-settlers-card-new");

            button.OnPressed += _ => PlayCard(kind);
            _cards.AddChild(button);
        }

        if (_cards.ChildCount == 0)
            _cards.AddChild(new Label { Text = Loc.GetString("wg-settlers-no-cards"), FontColorOverride = SettlersArt.Muted });
    }

    private static int Count(List<SettlersCard> cards, SettlersCard kind)
    {
        var count = 0;
        foreach (var card in cards)
        {
            if (card == kind)
                count++;
        }

        return count;
    }

    private void PlayCard(SettlersCard card)
    {
        if (card is SettlersCard.Windfall or SettlersCard.Embargo)
        {
            _pendingCard = card;
            _firstPick = null;
            _choiceLabel.Text = Loc.GetString(card == SettlersCard.Windfall ? "wg-settlers-choose-windfall" : "wg-settlers-choose-embargo");
            _choice.Visible = true;
            return;
        }

        OnAction?.Invoke(new SettlersAction(SettlersActionKind.PlayCard, (int) card));
    }

    private void PickResource(int resource)
    {
        if (_pendingCard is not { } card)
            return;

        if (card == SettlersCard.Windfall && _firstPick == null)
        {
            _firstPick = resource;
            _choiceLabel.Text = Loc.GetString("wg-settlers-choose-windfall-second", ("first", SettlersArt.ResourceName(resource)));
            return;
        }

        OnAction?.Invoke(new SettlersAction(SettlersActionKind.PlayCard, (int) card, _firstPick ?? resource, resource));
        CancelCard();
    }

    private void CancelCard()
    {
        _pendingCard = null;
        _firstPick = null;
        _choice.Visible = false;
    }

    private void RefreshSteal(SettlersState state, int seat, bool myTurn)
    {
        _steal.RemoveAllChildren();
        _steal.Visible = myTurn && state.Phase == SettlersPhase.Steal;
        if (!_steal.Visible)
            return;

        _steal.AddChild(new Label { Text = Loc.GetString("wg-settlers-rob") });
        foreach (var victim in state.Victims(seat, state.Pirates))
        {
            var target = victim;
            var button = new Button { Text = SeatName(victim) };
            button.OnPressed += _ => OnAction?.Invoke(new SettlersAction(SettlersActionKind.Steal, target));
            _steal.AddChild(button);
        }
    }

    private void RefreshDiscard(SettlersState state, int seat)
    {
        var owed = state.Phase == SettlersPhase.Discard ? state.Discard[seat] : 0;
        _discardBox.Visible = owed > 0;
        if (owed == 0)
            return;

        _discard.SetLimits(state.Players[seat].Hand);
        var chosen = SettlersRules.Total(_discard.Counts);
        _discardLabel.Text = Loc.GetString("wg-settlers-discard-count", ("count", owed), ("chosen", chosen));
        _discardButton.Disabled = chosen != owed;
    }

    private void RefreshTrade(SettlersState state, int seat, bool main)
    {
        var hand = state.Players[seat].Hand;
        var give = _bankGive.SelectedId;
        var get = _bankGet.SelectedId;
        var ratio = state.TradeRatio(seat, give);
        _bankTrade.Text = Loc.GetString("wg-settlers-bank-trade", ("ratio", ratio));
        _bankTrade.Disabled = !main || give == get || hand[give] < ratio || state.Bank[get] < 1;

        var trading = state.Phase == SettlersPhase.Main;
        _offerGive.SetLimits(hand);
        _postOffer.Disabled = !trading || SettlersRules.Total(_offerGive.Counts) == 0 || SettlersRules.Total(_offerWant.Counts) == 0;
        _withdrawOffer.Visible = state.Offers[seat] != null;

        _offers.RemoveAllChildren();
        for (var offerer = 0; offerer < state.Offers.Length; offerer++)
        {
            if (state.Offers[offerer] is not { } offer)
                continue;

            var swatch = new PanelContainer
            {
                PanelOverride = new StyleBoxFlat { BackgroundColor = SettlersArt.SeatColors[offerer] },
                MinWidth = 4f,
                VerticalExpand = true,
            };

            var name = new Label { Text = SeatName(offerer), ClipText = true, FontColorOverride = SettlersArt.Muted };
            name.StyleClasses.Add("LabelSubText");
            var terms = Row(Chips(offer.Give), Muted("wg-settlers-offer-for"), Chips(offer.Want));
            var text = Column(0);
            text.HorizontalExpand = true;
            text.AddChild(name);
            text.AddChild(terms);

            var row = Row(swatch, text);
            var mayAccept = offerer != seat && (offerer == state.Turn || seat == state.Turn) && state.CanAfford(seat, offer.Want);
            if (offerer != seat && trading)
            {
                var from = offerer;
                var accept = new Button { Text = Loc.GetString("wg-settlers-accept"), Disabled = !mayAccept, VerticalAlignment = VAlignment.Center };
                var seenGive = (int[]) offer.Give.Clone();
                var seenWant = (int[]) offer.Want.Clone();
                accept.OnPressed += _ => OnAction?.Invoke(new SettlersAction(SettlersActionKind.Accept, from) { Give = seenGive, Want = seenWant });
                row.AddChild(accept);
            }

            var card = new PanelContainer { PanelOverride = SettlersArt.Box(CardColor, CardEdge, 1f, 6f, 4f) };
            card.AddChild(row);
            _offers.AddChild(card);
        }
    }

    private void RefreshLog(SettlersState state)
    {
        _log.RemoveAllChildren();
        for (var i = state.Log.Count - 1; i >= 0; i--)
        {
            var entry = state.Log[i];
            var label = new Label { Text = LogLine(entry), ClipText = true };
            label.StyleClasses.Add("LabelSubText");
            label.FontColorOverride = i == state.Log.Count - 1 ? null : SettlersArt.Muted;
            _log.AddChild(label);
        }
    }

    private string LogLine(SettlersLogEntry entry)
    {
        var player = SeatName(entry.Seat);
        var other = entry.Other >= 0 && entry.Other < SettlersRules.MaxPlayers ? SeatName(entry.Other) : string.Empty;
        return entry.Kind switch
        {
            SettlersLogKind.Built => Loc.GetString(entry.Value == 0 ? "wg-settlers-log-lane" : "wg-settlers-log-outpost", ("player", player)),
            SettlersLogKind.PlayedCard => Loc.GetString("wg-settlers-log-playedcard", ("player", player), ("card", SettlersArt.CardName((SettlersCard) entry.Value))),
            SettlersLogKind.BankTrade => Loc.GetString("wg-settlers-log-banktrade", ("player", player),
                ("give", SettlersArt.ResourceName(entry.Other)), ("get", SettlersArt.ResourceName(entry.Value))),
            _ => Loc.GetString($"wg-settlers-log-{entry.Kind.ToString().ToLowerInvariant()}", ("player", player), ("other", other), ("value", entry.Value)),
        };
    }

    private void OnPicked(SettlersPick pick, int target)
    {
        var kind = pick switch
        {
            SettlersPick.Lane => SettlersActionKind.BuildLane,
            SettlersPick.Outpost => SettlersActionKind.BuildOutpost,
            SettlersPick.Colony => SettlersActionKind.BuildColony,
            _ => SettlersActionKind.MovePirates,
        };

        OnAction?.Invoke(new SettlersAction(kind, target));
    }

    private void Act(SettlersActionKind kind)
    {
        OnAction?.Invoke(new SettlersAction(kind));
    }

    private void SendCounts(SettlersActionKind kind, int[] give, int[] want)
    {
        OnAction?.Invoke(new SettlersAction(kind)
        {
            Give = (int[]) give.Clone(),
            Want = (int[]) want.Clone(),
        });

        if (kind == SettlersActionKind.Offer)
        {
            _offerGive.Clear();
            _offerWant.Clear();
            Refresh();
        }
    }
}
