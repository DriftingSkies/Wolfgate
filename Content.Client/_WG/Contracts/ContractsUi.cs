using System.Linq;
using Content.Client.UserInterface.Fragments;
using Content.Shared._WG.Contracts;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WG.Contracts;

/// <summary>The Contracts PDA app: the board in a pocket. Offers, the crew's contracts, history and standing.</summary>
public sealed partial class ContractsUi : UIFragment
{
    private ContractsUiFragment? _fragment;
    private BoundUserInterface? _owner;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    /// <summary>Reuses the fragment when the PDA calls this again for the same window.</summary>
    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        if (_fragment != null && _owner == userInterface && !_fragment.Disposed)
            return;

        _owner = userInterface;
        _fragment = new ContractsUiFragment();
        _fragment.OnAccept += id => userInterface.SendMessage(new CartridgeUiMessage(new WGContractCartridgeAcceptMessage(id)));
        _fragment.OnAbandon += id => userInterface.SendMessage(new CartridgeUiMessage(new WGContractCartridgeAbandonMessage(id)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is WGContractCartridgeUiState cast)
            _fragment?.UpdateState(cast.State);
    }
}

/// <summary>Two pages to fit the PDA: the list (tabs, category picker, rows) and the picked contract's details.</summary>
public sealed partial class ContractsUiFragment : BoxContainer
{
    [Dependency] private IGameTiming _timing = default!;

    public event Action<int>? OnAccept;
    public event Action<int>? OnAbandon;

    private readonly Dictionary<WGContractBoardWindow.Tab, Button> _tabs = new();
    private readonly Label _status;
    private readonly OptionButton _category;
    private readonly BoxContainer _listPage;
    private readonly BoxContainer _list;
    private readonly ScrollContainer _listScroll;
    private readonly ScrollContainer _standingScroll;
    private readonly WGContractStandingList _standing;
    private readonly BoxContainer _detailPage;
    private readonly Label _detailHeader;
    private readonly WGContractDetails _details;
    private readonly List<WGContractRow> _rows = new();
    private readonly List<WGContractCategory?> _categories = new();

    private WGContractBoardState? _state;
    private WGContractBoardWindow.Tab _tab = WGContractBoardWindow.Tab.Offers;
    private WGContractCategory? _filter;
    private int? _open;
    private int? _confirm;
    private int? _accepting;
    private TimeSpan _nextTick;

    public ContractsUiFragment()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(4);

        _listPage = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalExpand = true };
        AddChild(_listPage);

        var tabs = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        foreach (var tab in Enum.GetValues<WGContractBoardWindow.Tab>())
        {
            var button = new Button { ToggleMode = true, HorizontalExpand = true, ClipText = true };
            button.OnPressed += _ =>
            {
                _accepting = null;
                SetTab(tab);
            };
            _tabs[tab] = button;
            tabs.AddChild(button);
        }
        _listPage.AddChild(tabs);

        _status = new Label { FontColorOverride = WGContractStyle.Dim, Margin = new Thickness(0, 2, 0, 2), ClipText = true };
        _listPage.AddChild(_status);

        _category = new OptionButton { HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        _category.OnItemSelected += args =>
        {
            _category.SelectId(args.Id);
            _filter = _categories[args.Id];
            Rebuild();
        };
        _listPage.AddChild(_category);

        _listScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _list = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        _listScroll.AddChild(_list);
        _listPage.AddChild(_listScroll);

        _standing = new WGContractStandingList();
        _standingScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Visible = false };
        _standingScroll.AddChild(_standing);
        _listPage.AddChild(_standingScroll);

        _detailPage = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalExpand = true, Visible = false };
        var bar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var back = new Button { Text = Loc.GetString("wg-contract-pda-back") };
        back.OnPressed += _ => Close();
        bar.AddChild(back);
        _detailHeader = new Label
        {
            FontColorOverride = WGContractStyle.Dim, HorizontalExpand = true, ClipText = true, Margin = new Thickness(6, 0, 0, 0),
        };
        bar.AddChild(_detailHeader);
        _detailPage.AddChild(bar);
        _details = new WGContractDetails(_timing);
        _details.OnAction += OnDetailsAction;
        _detailPage.AddChild(_details);
        AddChild(_detailPage);

        SetTab(WGContractBoardWindow.Tab.Offers);
    }

    public void UpdateState(WGContractBoardState state)
    {
        _state = state;
        _tabs[WGContractBoardWindow.Tab.Offers].Text = Loc.GetString("wg-contract-pda-tab-offers", ("count", state.Offers.Count));
        _tabs[WGContractBoardWindow.Tab.Accepted].Text = Loc.GetString("wg-contract-pda-tab-accepted", ("count", state.Active.Count),
            ("max", state.MaxActive));
        _tabs[WGContractBoardWindow.Tab.History].Text = Loc.GetString("wg-contract-tab-history");
        _tabs[WGContractBoardWindow.Tab.Standing].Text = Loc.GetString("wg-contract-tab-standing");
        _status.Text = !state.CanHold
            ? Loc.GetString("wg-contract-board-no-character")
            : state.Owed > 0
                ? Loc.GetString("wg-contract-board-owed", ("pay", state.Owed))
                : Loc.GetString("wg-contract-pda-hint", ("offers", state.Offers.Count), ("crew", state.CrewSize),
                    ("slots", state.MaxActive), ("next", state.NextSlotAt));
        _standing.Update(state.Standing);

        // An offer just taken from its page follows the player to the Accepted tab.
        if (_accepting is { } taken && state.Active.Any(c => c.Id == taken && c.Mine))
        {
            _accepting = null;
            SetTab(WGContractBoardWindow.Tab.Accepted);
            _open = taken;
        }

        BuildCategories();
        Rebuild();
    }

    private void SetTab(WGContractBoardWindow.Tab tab)
    {
        _tab = tab;
        foreach (var (key, button) in _tabs)
        {
            button.Pressed = key == tab;
        }

        _open = null;
        _confirm = null;
        var standing = tab == WGContractBoardWindow.Tab.Standing;
        _standingScroll.Visible = standing;
        _listScroll.Visible = !standing;
        _category.Visible = tab == WGContractBoardWindow.Tab.Offers;
        Rebuild();
    }

    /// <summary>The category picker: All, then each category with its offer count.</summary>
    private void BuildCategories()
    {
        if (_state == null)
            return;

        _category.Clear();
        _categories.Clear();
        _categories.Add(null);
        _category.AddItem(Loc.GetString("wg-contract-rail-entry", ("name", Loc.GetString("wg-contract-category-all")),
            ("count", _state.Offers.Count)), 0);
        foreach (var category in Enum.GetValues<WGContractCategory>())
        {
            _category.AddItem(Loc.GetString("wg-contract-rail-entry", ("name", WGContractStyle.Category(category)),
                ("count", _state.Offers.Count(c => WGContractStyle.InCategory(c, category)))), _categories.Count);
            _categories.Add(category);
        }

        _category.SelectId(Math.Max(0, _categories.IndexOf(_filter)));
    }

    private List<WGContractView> Shown()
    {
        if (_state == null)
            return new List<WGContractView>();

        return _tab switch
        {
            WGContractBoardWindow.Tab.Offers => _state.Offers.Where(c => WGContractStyle.InCategory(c, _filter))
                .OrderBy(c => c.Locked != null)
                .ThenBy(c => c.Tier)
                .ThenByDescending(c => c.Reward)
                .ToList(),
            WGContractBoardWindow.Tab.Accepted => _state.Active,
            WGContractBoardWindow.Tab.History => _state.History,
            _ => new List<WGContractView>(),
        };
    }

    private void Rebuild()
    {
        _list.RemoveAllChildren();
        _rows.Clear();
        if (_state == null)
            return;

        var shown = Shown();
        if (shown.Count == 0)
        {
            _list.AddChild(WGContractStyle.Wrapped(Loc.GetString(_tab switch
            {
                WGContractBoardWindow.Tab.Offers when _filter == WGContractCategory.Planetary => "wg-contract-empty-planetary",
                WGContractBoardWindow.Tab.Offers => "wg-contract-empty-offers",
                WGContractBoardWindow.Tab.Accepted => "wg-contract-empty-accepted",
                _ => "wg-contract-empty-history",
            }), WGContractStyle.Dim, new Thickness(4)));
        }

        foreach (var contract in shown)
        {
            var row = new WGContractRow(contract, _timing);
            row.OnPressed += _ => Open(contract.Id);
            _rows.Add(row);
            _list.AddChild(row);
        }

        // The open page follows its contract; once it leaves the list, back to the list.
        if (_open is { } open && shown.All(c => c.Id != open))
            _open = null;

        ShowPage();
    }

    private void Open(int id)
    {
        _open = id;
        _confirm = null;
        ShowPage();
    }

    private void Close()
    {
        // A refused accept is not followed later, when a crewmate takes the same offer.
        _accepting = null;
        _open = null;
        _confirm = null;
        ShowPage();
    }

    private void ShowPage()
    {
        var contract = _open is { } id ? Shown().FirstOrDefault(c => c.Id == id) : null;
        _detailPage.Visible = contract != null;
        _listPage.Visible = contract == null;
        if (contract == null)
        {
            _details.Clear();
            return;
        }

        _detailHeader.Text = Loc.GetString("wg-contract-pda-detail-header", ("tab", _tabs[_tab].Text ?? string.Empty));
        switch (_tab)
        {
            case WGContractBoardWindow.Tab.Offers:
                _details.Show(contract, Loc.GetString("wg-contract-action-accept", ("deposit", contract.Deposit)), contract.Locked);
                break;
            case WGContractBoardWindow.Tab.Accepted:
                _details.Show(contract, !contract.Mine
                    ? null
                    : Loc.GetString(_confirm == contract.Id ? "wg-contract-action-abandon-confirm" : "wg-contract-action-abandon",
                        ("deposit", contract.Deposit)));
                break;
            default:
                _details.Show(contract, null);
                break;
        }
    }

    private void OnDetailsAction(WGContractView contract)
    {
        if (_tab == WGContractBoardWindow.Tab.Offers)
        {
            _accepting = contract.Id;
            OnAccept?.Invoke(contract.Id);
            return;
        }

        if (_tab != WGContractBoardWindow.Tab.Accepted)
            return;

        // Abandoning loses the deposit, so it takes a second press.
        if (_confirm != contract.Id)
        {
            _confirm = contract.Id;
            ShowPage();
            return;
        }

        _confirm = null;
        OnAbandon?.Invoke(contract.Id);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_timing.RealTime < _nextTick)
            return;

        _nextTick = _timing.RealTime + TimeSpan.FromSeconds(1);
        foreach (var row in _rows)
        {
            row.UpdateTime();
        }

        _details.UpdateTime();
    }
}
