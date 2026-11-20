using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Fragments;
using Content.Shared._WG.Paintings;
using Content.Shared.CartridgeLoader;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Paintings;

/// <summary>The art PDA app: the painting catalogue, prints to order, and the player's own scanned paintings.</summary>
public sealed partial class WGArtAppUi : UIFragment
{
    private WGArtAppFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new WGArtAppFragment();
        _fragment.OnAction += (action, page, text) =>
            userInterface.SendMessage(new CartridgeUiMessage(new WGArtAppMessage(action, page, text)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is WGArtAppUiState cast)
            _fragment?.UpdateState(cast);
    }
}

/// <summary>Tabs, a search box, a page of paintings with thumbnails, and a pager.</summary>
public sealed partial class WGArtAppFragment : BoxContainer
{
    [Dependency] private IEntityManager _entities = default!;

    private static readonly Color Dim = Color.FromHex("#9aa7b1");
    private static readonly Color Accent = Color.FromHex("#e0b050");
    private static readonly Color CardBackground = Color.FromHex("#1d2127");

    public event Action<WGArtAppAction, int, string>? OnAction;

    /// <summary>Null outside a running game, such as a UI test with no entity systems.</summary>
    private readonly WGPaintingArtSystem? _art;

    private readonly Button _catalogue;
    private readonly Button _mine;
    private readonly LineEdit _search;
    private readonly BoxContainer _list;
    private readonly Label _pageLabel;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly List<(TextureRect Rect, string Hash)> _thumbnails = new();

    private WGArtAppUiState? _state;

    public WGArtAppFragment()
    {
        IoCManager.InjectDependencies(this);
        _entities.EntitySysManager.TryGetEntitySystem(out _art);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(5);
        SeparationOverride = 4;

        var tabs = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _catalogue = new Button { Text = Loc.GetString("wg-art-app-tab-catalogue"), ToggleMode = true };
        _catalogue.OnPressed += _ => OnAction?.Invoke(WGArtAppAction.ShowCatalogue, 0, string.Empty);
        _mine = new Button { Text = Loc.GetString("wg-art-app-tab-mine"), ToggleMode = true };
        _mine.OnPressed += _ => OnAction?.Invoke(WGArtAppAction.ShowMine, 0, string.Empty);
        tabs.AddChild(_catalogue);
        tabs.AddChild(_mine);
        AddChild(tabs);

        _search = new LineEdit
        {
            PlaceHolder = Loc.GetString("wg-art-app-search"),
            HorizontalExpand = true,
            IsValid = text => text.Length <= WGPaintingRules.MaxTitleLength,
        };
        _search.OnTextEntered += args => OnAction?.Invoke(WGArtAppAction.Search, 0, args.Text);
        AddChild(_search);

        _list = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_list);
        AddChild(scroll);

        var pager = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalAlignment = HAlignment.Center, SeparationOverride = 8 };
        _previous = new Button { Text = "<" };
        _previous.OnPressed += _ => Turn(-1);
        _pageLabel = new Label { VerticalAlignment = VAlignment.Center };
        _next = new Button { Text = ">" };
        _next.OnPressed += _ => Turn(1);
        pager.AddChild(_previous);
        pager.AddChild(_pageLabel);
        pager.AddChild(_next);
        AddChild(pager);

        AddChild(new Label
        {
            Text = Loc.GetString("wg-art-app-scan-hint"),
            FontColorOverride = Dim,
            StyleClasses = { "LabelSmall" },
        });
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (_art != null)
            _art.ArtArrived += UpdateThumbnails;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (_art != null)
            _art.ArtArrived -= UpdateThumbnails;
    }

    private void Turn(int by)
    {
        if (_state is { } state)
            OnAction?.Invoke(WGArtAppAction.Page, state.Page + by, string.Empty);
    }

    public void UpdateState(WGArtAppUiState state)
    {
        _state = state;
        _catalogue.Pressed = !state.Mine;
        _mine.Pressed = state.Mine;
        if (!_search.HasKeyboardFocus())
            _search.Text = state.Search;
        _pageLabel.Text = Loc.GetString("wg-art-app-page", ("page", state.Page + 1), ("pages", state.Pages));
        _previous.Disabled = state.Page <= 0;
        _next.Disabled = state.Page >= state.Pages - 1;

        _list.RemoveAllChildren();
        _thumbnails.Clear();
        if (!state.Available)
        {
            _list.AddChild(new Label { Text = Loc.GetString("wg-art-app-unavailable"), FontColorOverride = Dim });
            return;
        }

        if (state.Entries.Count == 0)
        {
            _list.AddChild(new Label
            {
                Text = Loc.GetString(state.Mine ? "wg-art-app-none-mine" : "wg-art-app-none"),
                FontColorOverride = Dim,
            });
            return;
        }

        foreach (var entry in state.Entries)
            _list.AddChild(Row(entry, state.Price));

        UpdateThumbnails();
    }

    private Control Row(WGArtAppEntry entry, int price)
    {
        var thumbnail = new TextureRect
        {
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            MinSize = new Vector2(96, 64),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _thumbnails.Add((thumbnail, entry.Hash));

        var text = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        text.AddChild(new Label { Text = entry.Title, FontColorOverride = Accent, ClipText = true });
        text.AddChild(new Label
        {
            Text = Loc.GetString("wg-art-app-entry", ("artist", entry.Artist), ("width", entry.Width),
                ("height", entry.Height)),
            FontColorOverride = Dim,
            StyleClasses = { "LabelSmall" },
            ClipText = true,
        });

        var buttons = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalAlignment = VAlignment.Center };
        var order = new ConfirmButton
        {
            Text = Loc.GetString("wg-art-app-order", ("price", price)),
            ConfirmationText = Loc.GetString("wg-art-app-order-confirm", ("price", price)),
        };
        order.OnPressed += _ => OnAction?.Invoke(WGArtAppAction.Order, 0, entry.Hash);
        buttons.AddChild(order);
        if (entry.Mine)
        {
            var remove = new ConfirmButton
            {
                Text = Loc.GetString("wg-art-app-remove"),
                ConfirmationText = Loc.GetString("wg-art-app-remove-confirm"),
            };
            remove.OnPressed += _ => OnAction?.Invoke(WGArtAppAction.Remove, 0, entry.Hash);
            buttons.AddChild(remove);
        }

        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        row.AddChild(thumbnail);
        row.AddChild(text);
        row.AddChild(buttons);

        var card = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = CardBackground,
                BorderColor = Accent,
                BorderThickness = new Thickness(3, 0, 0, 0),
                ContentMarginLeftOverride = 8,
                ContentMarginTopOverride = 4,
                ContentMarginRightOverride = 6,
                ContentMarginBottomOverride = 4,
            },
        };
        card.AddChild(row);
        return card;
    }

    private void UpdateThumbnails()
    {
        if (_art == null)
            return;

        foreach (var (rect, hash) in _thumbnails)
        {
            if (rect.Texture == null)
                rect.Texture = _art.GetArt(hash);
        }
    }
}
