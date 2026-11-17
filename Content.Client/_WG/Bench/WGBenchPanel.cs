using System.Linq;
using Content.Shared._WG.Bench;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WG.Bench;

/// <summary>The Bench's tools, shared by the PDA app and the terminal: write a ticket, read the ledger, read and post laws.</summary>
public sealed class WGBenchPanel : BoxContainer
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Robust.Shared.Configuration.IConfigurationManager _cfg = default!;

    private enum Tab
    {
        Ticket,
        Ledger,
        Laws,
    }

    public event Action<WGBenchAction>? OnAction;

    private readonly Dictionary<Tab, Button> _tabs = new();
    private readonly Dictionary<Tab, Control> _pages = new();
    private Tab _tab = Tab.Ticket;
    private WGBenchUiState? _state;

    // Ticket page.
    private readonly OptionButton _offender = new() { HorizontalExpand = true };
    private readonly List<NetEntity> _offenders = new();
    private NetEntity? _selectedOffender;
    private readonly LineEdit _ship = new() { HorizontalExpand = true };
    private readonly LineEdit _notes = new() { HorizontalExpand = true };
    private readonly List<LawRow> _lawRows = new();
    private readonly BoxContainer _ordinanceRows = new() { Orientation = LayoutOrientation.Vertical };
    private readonly List<OrdinanceRow> _ordinanceRowList = new();
    private string _ordinanceKey = string.Empty;
    private readonly LineEdit _customTitle = new() { HorizontalExpand = true };
    private readonly LineEdit _customFine = new() { MinWidth = 80 };
    private readonly OptionButton _customLevel = new();
    private readonly BoxContainer _customList = new() { Orientation = LayoutOrientation.Vertical };
    private readonly List<WGBenchChargeRequest> _customs = new();
    private readonly Label _summary = new();
    private readonly Button _issue = new();

    // Ledger page.
    private readonly BoxContainer _ledgerList = new() { Orientation = LayoutOrientation.Vertical };
    private readonly RichTextLabel _ledgerDetails = new() { HorizontalExpand = true };
    private readonly Button _pardon = new();
    private int? _selectedTicket;
    private bool _confirmPardon;

    // Laws page.
    private readonly RichTextLabel _lawText = new() { HorizontalExpand = true };
    private bool _lawsBuilt;
    private readonly BoxContainer _postedList = new() { Orientation = LayoutOrientation.Vertical };
    private readonly BoxContainer _postForm = new() { Orientation = LayoutOrientation.Vertical };
    private readonly LineEdit _postTitle = new() { HorizontalExpand = true };
    private readonly LineEdit _postText = new() { HorizontalExpand = true };
    private readonly LineEdit _postFine = new() { MinWidth = 80 };
    private readonly OptionButton _postLevel = new();

    public WGBenchPanel()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(4);

        var tabs = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        AddChild(tabs);
        foreach (var tab in Enum.GetValues<Tab>())
        {
            var button = new Button
            {
                ToggleMode = true,
                MinWidth = 90,
                Text = Loc.GetString($"wg-bench-tab-{tab.ToString().ToLowerInvariant()}"),
            };
            button.OnPressed += _ => SetTab(tab);
            _tabs[tab] = button;
            tabs.AddChild(button);
        }

        _pages[Tab.Ticket] = BuildTicketPage();
        _pages[Tab.Ledger] = BuildLedgerPage();
        _pages[Tab.Laws] = BuildLawsPage();
        foreach (var page in _pages.Values)
        {
            AddChild(page);
        }

        SetTab(Tab.Ticket);
    }

    public void UpdateState(WGBenchUiState state)
    {
        _state = state;
        _tabs[Tab.Ticket].Visible = state.Judge;
        _tabs[Tab.Ledger].Visible = state.Judge;
        _postForm.Visible = state.Judge;
        if (!state.Judge && _tab != Tab.Laws)
            SetTab(Tab.Laws);

        UpdateOffenders(state);
        UpdateOrdinanceRows(state);
        UpdateLedger(state);
        UpdateLaws(state);
        UpdateSummary();
    }

    private void SetTab(Tab tab)
    {
        _tab = tab;
        foreach (var (key, button) in _tabs)
        {
            button.Pressed = key == tab;
        }

        foreach (var (key, page) in _pages)
        {
            page.Visible = key == tab;
        }
    }

    private Control BuildTicketPage()
    {
        var page = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalExpand = true };
        page.AddChild(Field("wg-bench-field-offender", _offender));
        _offender.OnItemSelected += args =>
        {
            _offender.SelectId(args.Id);
            _selectedOffender = args.Id >= 0 && args.Id < _offenders.Count ? _offenders[args.Id] : null;
            UpdateSummary();
        };
        _ship.PlaceHolder = Loc.GetString("wg-bench-field-ship-placeholder");
        page.AddChild(Field("wg-bench-field-ship", _ship));

        var charges = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(charges);
        page.AddChild(scroll);

        foreach (var chapter in _proto.EnumeratePrototypes<WGLawChapterPrototype>().OrderBy(c => c.Order))
        {
            charges.AddChild(Heading(Loc.GetString(chapter.Name)));
            foreach (var law in _proto.EnumeratePrototypes<WGLawPrototype>()
                         .Where(l => l.Chapter == chapter.ID).OrderBy(l => l.Order))
            {
                var row = new LawRow(law);
                row.Changed += UpdateSummary;
                _lawRows.Add(row);
                charges.AddChild(row);
            }
        }

        charges.AddChild(Heading(Loc.GetString("wg-bench-heading-ordinances")));
        charges.AddChild(_ordinanceRows);

        charges.AddChild(Heading(Loc.GetString("wg-bench-heading-custom")));
        charges.AddChild(_customList);
        var custom = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        _customTitle.PlaceHolder = Loc.GetString("wg-bench-custom-title");
        _customFine.PlaceHolder = Loc.GetString("wg-bench-custom-fine");
        AddLevels(_customLevel);
        var add = new Button { Text = Loc.GetString("wg-bench-custom-add") };
        add.OnPressed += _ => AddCustom();
        custom.AddChild(_customTitle);
        custom.AddChild(_customFine);
        custom.AddChild(_customLevel);
        custom.AddChild(add);
        charges.AddChild(custom);

        _notes.PlaceHolder = Loc.GetString("wg-bench-field-notes-placeholder");
        page.AddChild(Field("wg-bench-field-notes", _notes));

        var footer = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _summary.HorizontalExpand = true;
        footer.AddChild(_summary);
        _issue.Text = Loc.GetString("wg-bench-issue");
        _issue.OnPressed += _ => Issue();
        footer.AddChild(_issue);
        page.AddChild(footer);
        return page;
    }

    private Control BuildLedgerPage()
    {
        var page = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalExpand = true };
        var listScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, MinHeight = 120 };
        listScroll.AddChild(_ledgerList);
        page.AddChild(listScroll);
        var detailScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, MinHeight = 120 };
        detailScroll.AddChild(_ledgerDetails);
        page.AddChild(detailScroll);
        _pardon.Text = Loc.GetString("wg-bench-pardon");
        _pardon.HorizontalAlignment = HAlignment.Right;
        _pardon.OnPressed += _ =>
        {
            if (_selectedTicket is not { } number)
                return;
            if (!_confirmPardon)
            {
                _confirmPardon = true;
                _pardon.Text = Loc.GetString("wg-bench-pardon-confirm");
                return;
            }

            _confirmPardon = false;
            _pardon.Text = Loc.GetString("wg-bench-pardon");
            OnAction?.Invoke(new WGBenchPardonAction(number));
        };
        page.AddChild(_pardon);
        return page;
    }

    private Control BuildLawsPage()
    {
        var page = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalExpand = true };
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        var column = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(column);
        page.AddChild(scroll);
        column.AddChild(_lawText);
        column.AddChild(_postedList);

        _postForm.AddChild(Heading(Loc.GetString("wg-bench-post-heading")));
        _postTitle.PlaceHolder = Loc.GetString("wg-bench-post-title");
        _postText.PlaceHolder = Loc.GetString("wg-bench-post-text");
        _postFine.PlaceHolder = Loc.GetString("wg-bench-custom-fine");
        AddLevels(_postLevel);
        _postForm.AddChild(_postTitle);
        _postForm.AddChild(_postText);
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        row.AddChild(_postFine);
        row.AddChild(_postLevel);
        var post = new Button { Text = Loc.GetString("wg-bench-post") };
        post.OnPressed += _ =>
        {
            if (string.IsNullOrWhiteSpace(_postTitle.Text))
                return;
            OnAction?.Invoke(new WGBenchPostOrdinanceAction
            {
                Title = _postTitle.Text,
                Text = _postText.Text,
                Fine = ParseFine(_postFine.Text),
                Level = (WGPenaltyLevel) _postLevel.SelectedId,
            });
            _postTitle.Text = string.Empty;
            _postText.Text = string.Empty;
            _postFine.Text = string.Empty;
        };
        row.AddChild(post);
        _postForm.AddChild(row);
        page.AddChild(_postForm);
        return page;
    }

    private void UpdateOffenders(WGBenchUiState state)
    {
        _offenders.Clear();
        _offender.Clear();
        var selected = -1;
        foreach (var person in state.Nearby)
        {
            if (person.Entity == _selectedOffender)
                selected = _offenders.Count;
            _offender.AddItem(person.Name, _offenders.Count);
            _offenders.Add(person.Entity);
        }

        if (_offenders.Count == 0)
        {
            _offender.AddItem(Loc.GetString("wg-bench-no-one-near"), -1);
            _offender.SelectId(-1);
            _selectedOffender = null;
            return;
        }

        if (selected < 0)
        {
            selected = 0;
            _selectedOffender = _offenders[0];
        }

        _offender.SelectId(selected);
    }

    private void UpdateOrdinanceRows(WGBenchUiState state)
    {
        var key = string.Join(",", state.Ordinances.Select(o => o.Id));
        if (key == _ordinanceKey)
            return;

        _ordinanceKey = key;
        var ticked = _ordinanceRowList.Where(r => r.Ticked).Select(r => r.Ordinance.Id).ToHashSet();
        _ordinanceRows.RemoveAllChildren();
        _ordinanceRowList.Clear();
        if (state.Ordinances.Count == 0)
            _ordinanceRows.AddChild(new Label { Text = Loc.GetString("wg-bench-none"), FontColorOverride = Color.Gray });
        foreach (var ordinance in state.Ordinances)
        {
            var row = new OrdinanceRow(ordinance, ticked.Contains(ordinance.Id));
            row.Changed += UpdateSummary;
            _ordinanceRowList.Add(row);
            _ordinanceRows.AddChild(row);
        }
    }

    private void AddCustom()
    {
        if (string.IsNullOrWhiteSpace(_customTitle.Text))
            return;

        var level = (WGPenaltyLevel) _customLevel.SelectedId;
        _customs.Add(new WGBenchChargeRequest
        {
            Title = _customTitle.Text.Trim(),
            Fine = level == WGPenaltyLevel.Outlawry ? 0 : Math.Clamp(ParseFine(_customFine.Text), 0, WGBenchMath.MaxCustomFine),
            Level = level,
        });
        _customTitle.Text = string.Empty;
        _customFine.Text = string.Empty;
        RebuildCustoms();
        UpdateSummary();
    }

    private void RebuildCustoms()
    {
        _customList.RemoveAllChildren();
        foreach (var charge in _customs.ToList())
        {
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
            row.AddChild(new Label
            {
                HorizontalExpand = true,
                Text = Loc.GetString("wg-bench-charge-line", ("title", charge.Title), ("level", LevelName(charge.Level)), ("fine", charge.Fine)),
            });
            var remove = new Button { Text = Loc.GetString("wg-bench-remove") };
            remove.OnPressed += _ =>
            {
                _customs.Remove(charge);
                RebuildCustoms();
                UpdateSummary();
            };
            row.AddChild(remove);
            _customList.AddChild(row);
        }
    }

    private List<WGBenchChargeRequest> Charges()
    {
        var charges = new List<WGBenchChargeRequest>();
        foreach (var row in _lawRows.Where(r => r.Ticked))
        {
            charges.Add(new WGBenchChargeRequest { Law = row.Law.ID, Fine = row.Fine, Level = row.Law.Level });
        }

        foreach (var row in _ordinanceRowList.Where(r => r.Ticked))
        {
            charges.Add(new WGBenchChargeRequest { Ordinance = row.Ordinance.Id, Fine = row.Ordinance.Fine, Level = row.Ordinance.Level });
        }

        charges.AddRange(_customs);
        return charges;
    }

    private void UpdateSummary()
    {
        var charges = Charges();
        var (total, level) = WGBenchMath.Sum(charges.Select(c => (c.Fine, c.Level)));
        var cap = _cfg.GetCVar(Content.Shared._WG.CCVar.BenchCVars.TicketCap);
        _summary.Text = charges.Count == 0
            ? Loc.GetString("wg-bench-summary-empty")
            : Loc.GetString("wg-bench-summary", ("count", charges.Count), ("total", Math.Min(total, cap)), ("level", LevelName(level)))
              + (total > cap ? Loc.GetString("wg-bench-summary-capped") : string.Empty);
        _issue.Disabled = charges.Count == 0 || charges.Count > WGBenchMath.MaxCharges || _selectedOffender == null;
    }

    private void Issue()
    {
        if (_selectedOffender is not { } offender)
            return;

        var charges = Charges();
        if (charges.Count == 0)
            return;

        OnAction?.Invoke(new WGBenchIssueAction
        {
            Offender = offender,
            Ship = _ship.Text,
            Notes = _notes.Text,
            Charges = charges,
        });

        foreach (var row in _lawRows)
        {
            row.Reset();
        }

        foreach (var row in _ordinanceRowList)
        {
            row.Reset();
        }

        _customs.Clear();
        RebuildCustoms();
        _ship.Text = string.Empty;
        _notes.Text = string.Empty;
        UpdateSummary();
    }

    private void UpdateLedger(WGBenchUiState state)
    {
        _ledgerList.RemoveAllChildren();
        if (state.Tickets.Count == 0)
            _ledgerList.AddChild(new Label { Text = Loc.GetString("wg-bench-ledger-empty"), FontColorOverride = Color.Gray });

        WGBenchTicketView? selected = null;
        foreach (var ticket in state.Tickets)
        {
            if (ticket.Number == _selectedTicket)
                selected = ticket;

            var button = new Button
            {
                ToggleMode = true,
                Pressed = ticket.Number == _selectedTicket,
                HorizontalExpand = true,
                Text = Loc.GetString("wg-bench-ledger-row", ("number", ticket.Number), ("name", ticket.Offender),
                    ("total", ticket.Total), ("status", StatusName(ticket.Status)), ("owed", ticket.Owed)),
            };
            var number = ticket.Number;
            button.OnPressed += _ =>
            {
                _selectedTicket = number;
                _confirmPardon = false;
                _pardon.Text = Loc.GetString("wg-bench-pardon");
                if (_state != null)
                    UpdateLedger(_state);
            };
            _ledgerList.AddChild(button);
        }

        if (selected == null)
        {
            _selectedTicket = null;
            _ledgerDetails.SetMessage(Loc.GetString("wg-bench-ledger-pick"));
            _pardon.Disabled = true;
            return;
        }

        var text = new FormattedMessage();
        text.AddMarkupOrThrow(Loc.GetString("wg-bench-ledger-head", ("number", selected.Number),
            ("name", FormattedMessage.EscapeText(selected.Offender)), ("judge", FormattedMessage.EscapeText(selected.Judge))));
        if (selected.Ship.Length > 0)
        {
            text.PushNewline();
            text.AddText(Loc.GetString("wg-bench-ledger-ship", ("ship", selected.Ship)));
        }

        foreach (var charge in selected.Charges)
        {
            text.PushNewline();
            text.AddText(Loc.GetString("wg-bench-charge-line", ("title", charge.Title), ("level", LevelName(charge.Level)), ("fine", charge.Fine)));
        }

        text.PushNewline();
        text.AddText(Loc.GetString("wg-bench-ledger-money", ("total", selected.Total), ("paid", selected.Paid), ("owed", selected.Owed),
            ("status", StatusName(selected.Status))));
        if (selected.Status is WGBenchTicketStatus.Owing)
        {
            var left = selected.Deadline - _timing.CurTime;
            text.PushNewline();
            text.AddText(Loc.GetString("wg-bench-ledger-deadline", ("minutes", Math.Max(0, (int) Math.Ceiling(left.TotalMinutes)))));
        }

        if (selected.Notes.Length > 0)
        {
            text.PushNewline();
            text.AddText(Loc.GetString("wg-bench-ledger-notes", ("notes", selected.Notes)));
        }

        _ledgerDetails.SetMessage(text);
        _pardon.Disabled = selected.Status == WGBenchTicketStatus.Pardoned;
    }

    private void UpdateLaws(WGBenchUiState state)
    {
        if (!_lawsBuilt)
        {
            _lawsBuilt = true;
            var text = new FormattedMessage();
            text.AddMarkupOrThrow(Loc.GetString("wg-bench-laws-preamble"));
            text.PushNewline();
            text.AddMarkupOrThrow(Loc.GetString("wg-bench-laws-preamble-text"));
            foreach (var chapter in _proto.EnumeratePrototypes<WGLawChapterPrototype>().OrderBy(c => c.Order))
            {
                text.PushNewline();
                text.PushNewline();
                text.AddMarkupOrThrow($"[bold]{FormattedMessage.EscapeText(Loc.GetString(chapter.Name))}[/bold]");
                foreach (var law in _proto.EnumeratePrototypes<WGLawPrototype>()
                             .Where(l => l.Chapter == chapter.ID).OrderBy(l => l.Order))
                {
                    text.PushNewline();
                    text.AddMarkupOrThrow(Loc.GetString("wg-bench-law-entry", ("name", Loc.GetString(law.Name)),
                        ("text", Loc.GetString(law.Text)), ("penalty", Penalty(law.Level, law.Fine))));
                }
            }

            _lawText.SetMessage(text);
        }

        _postedList.RemoveAllChildren();
        _postedList.AddChild(Heading(Loc.GetString("wg-bench-heading-ordinances")));
        if (state.Ordinances.Count == 0)
            _postedList.AddChild(new Label { Text = Loc.GetString("wg-bench-none"), FontColorOverride = Color.Gray });
        foreach (var ordinance in state.Ordinances)
        {
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
            var label = new RichTextLabel { HorizontalExpand = true };
            label.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString("wg-bench-ordinance-entry",
                ("name", FormattedMessage.EscapeText(ordinance.Title)), ("text", FormattedMessage.EscapeText(ordinance.Text)),
                ("penalty", Penalty(ordinance.Level, ordinance.Fine)), ("by", FormattedMessage.EscapeText(ordinance.PostedBy)))));
            row.AddChild(label);
            if (state.Judge)
            {
                var id = ordinance.Id;
                var remove = new Button { Text = Loc.GetString("wg-bench-remove"), VerticalAlignment = VAlignment.Top };
                remove.OnPressed += _ => OnAction?.Invoke(new WGBenchRemoveOrdinanceAction(id));
                row.AddChild(remove);
            }

            _postedList.AddChild(row);
        }
    }

    private static string Penalty(WGPenaltyLevel level, int fine)
    {
        return level == WGPenaltyLevel.Outlawry
            ? LevelName(level)
            : Loc.GetString("wg-bench-penalty-fine", ("level", LevelName(level)), ("fine", fine));
    }

    public static string LevelName(WGPenaltyLevel level)
    {
        return Loc.GetString($"wg-penalty-{level.ToString().ToLowerInvariant()}");
    }

    private static string StatusName(WGBenchTicketStatus status)
    {
        return Loc.GetString($"wg-bench-status-{status.ToString().ToLowerInvariant()}");
    }

    private static void AddLevels(OptionButton button)
    {
        foreach (var level in Enum.GetValues<WGPenaltyLevel>())
        {
            button.AddItem(LevelName(level), (int) level);
        }

        button.OnItemSelected += args => button.SelectId(args.Id);
        button.SelectId(0);
    }

    private static int ParseFine(string text)
    {
        var digits = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if (char.IsDigit(c))
                digits.Append(c);
        }

        return int.TryParse(digits.ToString(), out var fine) ? fine : 0;
    }

    private static Control Field(string label, Control control)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        row.AddChild(new Label { Text = Loc.GetString(label), MinWidth = 70 });
        row.AddChild(control);
        return row;
    }

    private static Label Heading(string text)
    {
        return new Label { Text = text, StyleClasses = { "LabelHeading" }, Margin = new Thickness(0, 6, 0, 2) };
    }

    /// <summary>A law to tick, with the fine the judge sets inside its range.</summary>
    private sealed class LawRow : BoxContainer
    {
        public readonly WGLawPrototype Law;
        public event Action? Changed;

        private readonly CheckBox _tick;
        private readonly LineEdit? _fine;

        public bool Ticked => _tick.Pressed;

        public int Fine => _fine == null
            ? 0
            : WGBenchMath.Clamp(ParseFine(_fine.Text), Law.MinFine, Law.MaxFine);

        public LawRow(WGLawPrototype law)
        {
            Law = law;
            Orientation = LayoutOrientation.Horizontal;
            _tick = new CheckBox { Text = Loc.GetString(law.Name), HorizontalExpand = true, ToolTip = Loc.GetString(law.Text) };
            _tick.OnToggled += _ => Changed?.Invoke();
            AddChild(_tick);
            AddChild(new Label
            {
                Text = law.Level == WGPenaltyLevel.Outlawry
                    ? LevelName(law.Level)
                    : Loc.GetString("wg-bench-law-range", ("level", LevelName(law.Level)), ("min", law.MinFine), ("max", law.MaxFine)),
                FontColorOverride = Color.Gray,
                Margin = new Thickness(4, 0),
            });
            if (law.Level == WGPenaltyLevel.Outlawry)
                return;

            _fine = new LineEdit { MinWidth = 80, Text = law.Fine.ToString() };
            _fine.OnTextChanged += _ => Changed?.Invoke();
            AddChild(_fine);
        }

        public void Reset()
        {
            _tick.Pressed = false;
            if (_fine != null)
                _fine.Text = Law.Fine.ToString();
        }
    }

    /// <summary>A posted ordinance to tick, at its posted fine.</summary>
    private sealed class OrdinanceRow : BoxContainer
    {
        public readonly WGBenchOrdinanceView Ordinance;
        public event Action? Changed;

        private readonly CheckBox _tick;

        public bool Ticked => _tick.Pressed;

        public OrdinanceRow(WGBenchOrdinanceView ordinance, bool ticked)
        {
            Ordinance = ordinance;
            Orientation = LayoutOrientation.Horizontal;
            _tick = new CheckBox { Text = ordinance.Title, HorizontalExpand = true, Pressed = ticked, ToolTip = ordinance.Text };
            _tick.OnToggled += _ => Changed?.Invoke();
            AddChild(_tick);
            AddChild(new Label { Text = Penalty(ordinance.Level, ordinance.Fine), FontColorOverride = Color.Gray });
        }

        public void Reset()
        {
            _tick.Pressed = false;
        }
    }
}
