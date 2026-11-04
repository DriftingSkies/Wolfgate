using System.Linq;
using Content.Client._WF.MameArcade.NeoGeo;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.MameArcade;

/// <summary>
/// The cabinet's start screen: what is happening on the cabinet, the player's games with a search, and the chosen
/// game's details and buttons.
/// </summary>
public sealed class MameArcadeLibraryView : BoxContainer
{
    private static readonly Color Card = Color.FromHex("#1c1c24");
    private static readonly Color CardEdge = Color.FromHex("#3a3a48");
    private static readonly Color Ready = Color.FromHex("#4cc265");
    private static readonly Color Incomplete = Color.FromHex("#e0a030");
    private static readonly Color Warning = Color.FromHex("#3a2418");

    private readonly Label _summary;
    private readonly PanelContainer _biosBanner;
    private readonly PanelContainer _cabinetCard;
    private readonly BoxContainer _lobbyRow;
    private readonly Label _lobbyLabel;
    private readonly Button _joinButton;
    private readonly Button _cancelButton;
    private readonly BoxContainer _showingRow;
    private readonly Label _showingLabel;
    private readonly Button _watchButton;

    private readonly LineEdit _search;
    private readonly CheckBox _readyOnly;
    private readonly ItemList _games;

    private readonly BoxContainer _detail;
    private readonly RichTextLabel _detailTitle;
    private readonly Label _detailMaker;
    private readonly Label _detailSet;
    private readonly Label _detailStatus;
    private readonly RichTextLabel _detailMissing;
    private readonly Label _detailState;
    private readonly Button _playButton;
    private readonly Button _hostButton;
    private readonly RichTextLabel _emptyHint;

    private readonly RichTextLabel _unused;
    private readonly Label _message;

    private List<Entry> _entries = new();
    private Func<string, bool> _hasState = _ => false;
    private bool _cabinetBusy;

    public event Action? ImportPressed;
    public event Action? FolderPressed;
    public event Action? RefreshPressed;
    public event Action? PlayPressed;
    public event Action? HostPressed;
    public event Action? JoinPressed;
    public event Action? CancelPressed;
    public event Action? WatchPressed;

    /// <summary>The game whose details show.</summary>
    public IMameGame? Selected { get; private set; }

    public MameArcadeLibraryView()
    {
        Orientation = LayoutOrientation.Vertical;
        VerticalExpand = true;
        SeparationOverride = 8;

        var import = new Button { Text = Loc.GetString("mame-arcade-import"), StyleClasses = { StyleBase.ButtonOpenRight } };
        import.OnPressed += _ => ImportPressed?.Invoke();
        var folder = new Button { Text = Loc.GetString("mame-arcade-open-folder"), StyleClasses = { StyleBase.ButtonOpenBoth } };
        folder.OnPressed += _ => FolderPressed?.Invoke();
        var refresh = new Button { Text = Loc.GetString("mame-arcade-refresh"), StyleClasses = { StyleBase.ButtonOpenLeft } };
        refresh.OnPressed += _ => RefreshPressed?.Invoke();

        _summary = new Label { StyleClasses = { StyleBase.StyleClassLabelSubText } };
        var header = new BoxContainer
        {
            Children =
            {
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    Children =
                    {
                        new Label { Text = Loc.GetString("mame-arcade-library-title"), StyleClasses = { StyleNano.StyleClassLabelHeadingBigger } },
                        _summary,
                    },
                },
                new BoxContainer { VerticalAlignment = VAlignment.Center, Children = { import, folder, refresh } },
            },
        };

        var bios = new RichTextLabel();
        bios.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("mame-arcade-no-bios")));
        _biosBanner = Panel(Warning, Incomplete, bios);
        _biosBanner.Visible = false;

        _lobbyLabel = new Label { HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
        _joinButton = new Button { Text = Loc.GetString("mame-arcade-lobby-join"), StyleClasses = { StyleNano.StyleClassButtonColorGreen } };
        _joinButton.OnPressed += _ => JoinPressed?.Invoke();
        _cancelButton = new Button { Text = Loc.GetString("mame-arcade-lobby-cancel") };
        _cancelButton.OnPressed += _ => CancelPressed?.Invoke();
        _lobbyRow = new BoxContainer { Children = { _lobbyLabel, _joinButton, _cancelButton } };

        _showingLabel = new Label { ClipText = true };
        _watchButton = new Button { Text = Loc.GetString("mame-arcade-watch"), HorizontalAlignment = HAlignment.Left };
        _watchButton.OnPressed += _ => WatchPressed?.Invoke();
        _showingRow = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Children = { _showingLabel, _watchButton },
        };

        _cabinetCard = Panel(Card, CardEdge, new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Children =
            {
                new Label { Text = Loc.GetString("mame-arcade-cabinet-heading"), StyleClasses = { StyleNano.StyleClassLabelKeyText } },
                _lobbyRow,
                _showingRow,
            },
        });
        _cabinetCard.Visible = false;

        _search = new LineEdit { HorizontalExpand = true, PlaceHolder = Loc.GetString("mame-arcade-search") };
        _search.OnTextChanged += _ => Fill();
        _readyOnly = new CheckBox { Text = Loc.GetString("mame-arcade-ready-only") };
        _readyOnly.OnToggled += _ => Fill();
        _games = new ItemList { VerticalExpand = true, SelectMode = ItemList.ItemListSelectMode.Single };
        _games.OnItemSelected += args => Select(_games[args.ItemIndex].Metadata as Entry);

        var list = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SizeFlagsStretchRatio = 1.1f,
            SeparationOverride = 4,
            Children =
            {
                new BoxContainer { SeparationOverride = 6, Children = { _search, _readyOnly } },
                _games,
            },
        };

        _detailTitle = new RichTextLabel();
        _detailMaker = new Label { StyleClasses = { StyleBase.StyleClassLabelSubText } };
        _detailSet = new Label { FontColorOverride = Color.Gray };
        _detailStatus = new Label();
        _detailMissing = new RichTextLabel();
        _detailState = new Label { FontColorOverride = Color.Gray };
        _playButton = new Button
        {
            Text = Loc.GetString("mame-arcade-play"),
            StyleClasses = { StyleNano.StyleClassButtonBig, StyleNano.StyleClassButtonColorGreen },
        };
        _playButton.OnPressed += _ => PlayPressed?.Invoke();
        _hostButton = new Button { Text = Loc.GetString("mame-arcade-host") };
        _hostButton.OnPressed += _ => HostPressed?.Invoke();
        var hostHint = new RichTextLabel();
        hostHint.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("mame-arcade-host-hint")), defaultColor: Color.Gray);

        _detail = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            VerticalExpand = true,
            SeparationOverride = 4,
            Children =
            {
                _detailTitle,
                _detailMaker,
                _detailSet,
                new Control { MinHeight = 6 },
                _detailStatus,
                _detailMissing,
                _detailState,
                new Control { VerticalExpand = true },
                _playButton,
                _hostButton,
                hostHint,
            },
        };

        _emptyHint = new RichTextLabel { VerticalExpand = true };
        var details = Panel(Card, CardEdge, new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            VerticalExpand = true,
            Children = { _detail, _emptyHint },
        });
        details.HorizontalExpand = true;
        details.VerticalExpand = true;

        _unused = new RichTextLabel { Visible = false };
        _message = new Label { FontColorOverride = Incomplete, Visible = false };

        Children.Add(header);
        Children.Add(_biosBanner);
        Children.Add(_cabinetCard);
        Children.Add(new BoxContainer { VerticalExpand = true, SeparationOverride = 8, Children = { list, details } });
        Children.Add(_message);
        Children.Add(_unused);
    }

    /// <summary>
    /// Lists the games the player can run, and the ones they hold part of. Other versions of a game they can run
    /// share most of its chips, so an incomplete set only shows if the player has a chip no ready game uses.
    /// </summary>
    public void SetLibrary(IMameChipSource source, int zipCount, List<string> unusedZips, Func<string, bool> hasState)
    {
        _hasState = hasState;
        var held = MameGames.All
            .Where(game => game.Chips.Values.Any(source.Has))
            .Select(game => new Entry(game, MameGames.Missing(game, source)))
            .ToList();
        var usedByReady = held
            .Where(e => e.Missing.Count == 0)
            .SelectMany(e => e.Set.Chips.Keys)
            .ToHashSet();

        _entries = held
            .Where(e => e.Missing.Count == 0 || e.Set.Chips.Values.Any(c => source.Has(c) && !usedByReady.Contains(c.Crc)))
            .OrderBy(e => e.Missing.Count > 0)
            .ThenBy(e => e.Set.Title)
            .ToList();

        var ready = _entries.Count(e => e.Missing.Count == 0);
        _summary.Text = Loc.GetString("mame-arcade-library-summary",
            ("ready", ready), ("incomplete", _entries.Count - ready), ("zips", zipCount));

        _biosBanner.Visible = held.Any(e => e.Set.Board == MameBoard.NeoGeo) && MameGames.LacksNeoGeoBios(source);

        _unused.Visible = unusedZips.Count > 0;
        if (_unused.Visible)
        {
            _unused.SetMessage(
                FormattedMessage.FromUnformatted(Loc.GetString("mame-arcade-unused-zips", ("zips", string.Join(", ", unusedZips)))),
                defaultColor: Color.Gray);
        }

        var hint = zipCount == 0 ? "mame-arcade-empty-library" : "mame-arcade-no-games";
        _emptyHint.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(hint)), defaultColor: Color.Gray);

        var keep = Selected?.Name;
        Selected = null;
        Fill();
        var entry = _entries.FirstOrDefault(e => e.Set.Name == keep) ?? _entries.FirstOrDefault();
        SelectInList(entry);
    }

    /// <summary>
    /// The cabinet's open 2-player game; null text hides it. While the cabinet is busy with a game or a lobby, Play
    /// and Host stay off.
    /// </summary>
    public void SetLobby(string? text, bool canJoin, bool canCancel, bool busy)
    {
        _lobbyRow.Visible = text != null;
        _lobbyLabel.Text = text ?? string.Empty;
        _joinButton.Visible = canJoin;
        _cancelButton.Visible = canCancel;
        _cabinetBusy = busy;
        UpdateCabinetCard();
        UpdateButtons();
    }

    /// <summary>Another player's game on the cabinet's screen; null text hides it.</summary>
    public void SetShowing(string? text, bool canWatch, bool full)
    {
        _showingRow.Visible = text != null;
        _showingLabel.Text = text ?? string.Empty;
        _watchButton.Visible = canWatch;
        _watchButton.Disabled = full;
        _watchButton.Text = Loc.GetString(full ? "mame-arcade-watch-full" : "mame-arcade-watch");
        UpdateCabinetCard();
    }

    /// <summary>A note such as a failed import or a crash; null clears it.</summary>
    public void SetMessage(string? text)
    {
        _message.Visible = text != null;
        _message.Text = text ?? string.Empty;
    }

    private void UpdateCabinetCard()
    {
        _cabinetCard.Visible = _lobbyRow.Visible || _showingRow.Visible;
    }

    /// <summary>Rebuilds the list from the search and filter.</summary>
    private void Fill()
    {
        var search = _search.Text.Trim();
        _games.Clear();

        foreach (var entry in _entries)
        {
            if (_readyOnly.Pressed && entry.Missing.Count > 0)
                continue;

            if (search.Length > 0
                && !entry.Set.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !entry.Set.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var item = _games.AddItem(
                Loc.GetString("mame-arcade-game-entry", ("title", entry.Set.Title), ("year", entry.Set.Year)),
                Texture.White,
                metadata: entry,
                iconScale: 8);
            item.IconModulate = entry.Missing.Count == 0 ? Ready : Incomplete;
            item.TooltipText = entry.Missing.Count == 0
                ? Loc.GetString("mame-arcade-game-ready")
                : Loc.GetString("mame-arcade-game-missing", ("count", entry.Missing.Count));
            if (entry.Set == Selected)
                item.Selected = true;
        }

        if (Selected == null)
            Select(null);
    }

    private void SelectInList(Entry? entry)
    {
        foreach (var item in _games)
        {
            if (item.Metadata == entry)
            {
                item.Selected = true;
                return;
            }
        }

        Select(entry);
    }

    private void Select(Entry? entry)
    {
        Selected = entry?.Set;
        _detail.Visible = entry != null;
        _emptyHint.Visible = entry == null && _entries.Count == 0;
        UpdateButtons();

        if (entry == null)
            return;

        var set = entry.Set;
        _detailTitle.SetMessage(FormattedMessage.FromMarkupOrThrow(
            $"[font size=16][bold]{FormattedMessage.EscapeText(set.Title)}[/bold][/font]"));

        var board = Loc.GetString(set.Board == MameBoard.Cps2 ? "mame-arcade-board-cps2" : "mame-arcade-board-neogeo");
        _detailMaker.Text = Loc.GetString("mame-arcade-detail-maker", ("year", set.Year), ("maker", set.Manufacturer), ("board", board));
        _detailSet.Text = set.Parent.Length > 0
            ? Loc.GetString("mame-arcade-detail-clone", ("set", set.Name), ("parent", set.Parent))
            : Loc.GetString("mame-arcade-detail-set", ("set", set.Name));

        var ready = entry.Missing.Count == 0;
        _detailStatus.Text = ready
            ? Loc.GetString("mame-arcade-detail-ready")
            : Loc.GetString("mame-arcade-game-missing", ("count", entry.Missing.Count));
        _detailStatus.FontColorOverride = ready ? Ready : Incomplete;

        _detailMissing.Visible = !ready;
        if (!ready)
        {
            _detailMissing.SetMessage(
                FormattedMessage.FromUnformatted(Loc.GetString("mame-arcade-missing-list",
                    ("files", string.Join(", ", entry.Missing.Select(m => m.Name))))),
                defaultColor: Color.Gray);
        }

        _detailState.Visible = _hasState(set.Name);
        _detailState.Text = Loc.GetString("mame-arcade-detail-state");
    }

    private void UpdateButtons()
    {
        var ready = _entries.FirstOrDefault(e => e.Set == Selected) is { Missing.Count: 0 };
        _playButton.Disabled = !ready || _cabinetBusy;
        _hostButton.Disabled = !ready || _cabinetBusy;
    }

    private static PanelContainer Panel(Color background, Color edge, Control content)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = edge,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 8,
            ContentMarginBottomOverride = 8,
        };

        return new PanelContainer { PanelOverride = box, Children = { content } };
    }

    private sealed record Entry(IMameGame Set, List<MameChip> Missing);
}
