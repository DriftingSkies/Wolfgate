using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipCommand;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.ShipCommand;

/// <summary>
/// The ship command console: a tab for each thing the owner runs (zones, people, prices, crew, orders, jobs, presets, the
/// recovery pads) and the service log for the owner and admins. Officers see every tab with its buttons off. Lists are
/// rebuilt from each state; the forms (a new price rule, a preset's name, the log filter) keep what was typed.
/// </summary>
public sealed class WGShipConsoleWindow : FancyWindow
{
    private static readonly Color Dim = Color.FromHex("#9aa4b1");
    private static readonly Color Warn = Color.FromHex("#e0a33a");

    /// <summary>The services a price rule can change: those that actually charge through the ship's prices.</summary>
    private static readonly WGShipServiceKind[] Services =
    {
        WGShipServiceKind.Vending, WGShipServiceKind.Counter, WGShipServiceKind.Drydock,
        WGShipServiceKind.Vehicles, WGShipServiceKind.Deposit, WGShipServiceKind.Shipyard,
    };

    private static readonly WGShipAuthority[] PadLevels = { WGShipAuthority.Admin, WGShipAuthority.Officer, WGShipAuthority.Staff };

    public event Action<BoundUserInterfaceMessage>? OnSend;

    private readonly Label _header;
    private readonly TabContainer _tabs;
    private readonly BoxContainer _zones;
    private readonly BoxContainer _people;
    private readonly BoxContainer _prices;
    private readonly BoxContainer _crew;
    private readonly BoxContainer _orders;
    private readonly BoxContainer _callsignForm;
    private readonly LineEdit _callsign;
    private readonly BoxContainer _jobs;
    private readonly BoxContainer _presets;
    private readonly BoxContainer _pads;
    private readonly BoxContainer _log;
    private readonly Control _logPage;

    private readonly BoxContainer _ruleForm;
    private readonly LineEdit _ruleName;
    private readonly OptionButton _ruleAudience;
    private readonly OptionButton _rulePerson;
    private readonly LineEdit _ruleCompany;
    private readonly LineEdit _ruleMultiplier;
    private readonly Dictionary<WGShipServiceKind, CheckBox> _ruleServices = new();
    private readonly Button _ruleAdd;
    private List<WGShipConsoleAboard> _personChoices = new();

    private readonly BoxContainer _presetForm;
    private readonly LineEdit _presetName;
    private readonly Button _presetSave;
    private string? _confirmDelete;

    private readonly LineEdit _logFilter;

    private WGShipConsoleState? _state;

    public WGShipConsoleWindow()
    {
        Title = Loc.GetString("wg-ship-console-title");
        SetSize = new Vector2(820, 640);
        MinSize = new Vector2(640, 420);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        ContentsContainer.AddChild(root);

        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        _header = new Label { HorizontalExpand = true };
        top.AddChild(_header);
        top.AddChild(Btn("wg-ship-console-refresh", () => Send(new WGShipConsoleRefreshMessage())));
        root.AddChild(top);

        _tabs = new TabContainer { VerticalExpand = true };
        root.AddChild(_tabs);

        _zones = Page("wg-ship-console-tab-zones", null);
        _people = Page("wg-ship-console-tab-people", null);

        // A new price rule.
        _ruleForm = VBox();
        _ruleName = new LineEdit { MinWidth = 160, PlaceHolder = Loc.GetString("wg-ship-console-rule-name") };
        _ruleAudience = new OptionButton { MinWidth = 140 };
        foreach (var audience in Enum.GetValues<WGShipPriceAudience>())
            _ruleAudience.AddItem(Loc.GetString($"wg-ship-console-audience-{audience.ToString().ToLowerInvariant()}"), (int) audience);
        _ruleAudience.OnItemSelected += args =>
        {
            _ruleAudience.SelectId(args.Id);
            UpdateRuleForm();
        };
        _rulePerson = new OptionButton { MinWidth = 180 };
        _rulePerson.OnItemSelected += args => _rulePerson.SelectId(args.Id);
        _ruleCompany = new LineEdit { MinWidth = 160, PlaceHolder = Loc.GetString("wg-ship-console-rule-company") };
        _ruleMultiplier = new LineEdit { MinWidth = 60, Text = "0" };
        _ruleAdd = Btn("wg-ship-console-rule-add", AddRule);
        _ruleForm.AddChild(Row(Lbl("wg-ship-console-rule-new"), _ruleName, _ruleAudience, _rulePerson, _ruleCompany));
        var services = HBox();
        foreach (var kind in Services)
        {
            var box = new CheckBox { Text = Loc.GetString($"wg-ship-console-service-{kind.ToString().ToLowerInvariant()}"), Pressed = true, Margin = new Thickness(0, 0, 10, 0) };
            _ruleServices[kind] = box;
            services.AddChild(box);
        }
        _ruleForm.AddChild(services);
        _ruleForm.AddChild(Row(Lbl("wg-ship-console-rule-multiplier"), _ruleMultiplier, _ruleAdd));
        _prices = Page("wg-ship-console-tab-prices", _ruleForm);

        _crew = Page("wg-ship-console-tab-crew", null);

        // The callsign, for setting.
        _callsign = new LineEdit { MinWidth = 320, PlaceHolder = Loc.GetString("wg-ship-console-callsign-placeholder") };
        _callsignForm = Row(_callsign, Btn("wg-ship-console-callsign-set", () => Send(new WGShipConsoleOrdersMessage(null, null, null, _callsign.Text))));
        _orders = Page("wg-ship-console-tab-orders", _callsignForm);

        _jobs = Page("wg-ship-console-tab-jobs", null);

        // A preset's name, for saving.
        _presetName = new LineEdit { MinWidth = 200, PlaceHolder = Loc.GetString("wg-ship-console-preset-name") };
        _presetSave = Btn("wg-ship-console-preset-save", () =>
        {
            var name = _presetName.Text.Trim();
            if (name.Length > 0)
                Send(new WGShipConsolePresetMessage(WGShipConsolePresetAction.Save, name));
        });
        _presetForm = Row(_presetName, _presetSave);
        _presets = Page("wg-ship-console-tab-presets", _presetForm);

        _pads = Page("wg-ship-console-tab-pads", null);

        _logFilter = new LineEdit { HorizontalExpand = true, PlaceHolder = Loc.GetString("wg-ship-console-log-filter") };
        _logFilter.OnTextChanged += _ => RebuildLog();
        _log = Page("wg-ship-console-tab-log", Row(_logFilter));
        _logPage = _tabs.GetChild(_tabs.ChildCount - 1);

        UpdateRuleForm();
    }

    #region Building blocks

    private void Send(BoundUserInterfaceMessage message)
    {
        OnSend?.Invoke(message);
    }

    /// <summary>A tab: an optional form that keeps its contents, over a list rebuilt from each state.</summary>
    private BoxContainer Page(string title, Control? form)
    {
        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
        var inner = VBox();
        inner.Margin = new Thickness(4);
        if (form != null)
            inner.AddChild(form);
        var body = VBox();
        inner.AddChild(body);
        scroll.AddChild(inner);
        _tabs.AddChild(scroll);
        TabContainer.SetTabTitle(scroll, Loc.GetString(title));
        return body;
    }

    private static BoxContainer VBox() => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };

    private static BoxContainer HBox() => new() { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };

    private static BoxContainer Row(params Control[] children)
    {
        var row = HBox();
        foreach (var child in children)
        {
            child.Margin = new Thickness(0, 0, 6, 0);
            row.AddChild(child);
        }

        return row;
    }

    private static Label Lbl(string key, params (string, object)[] args)
    {
        return new Label { Text = Loc.GetString(key, args) };
    }

    private static Label Text(string text, Color? color = null, bool expand = false)
    {
        return new Label { Text = text, FontColorOverride = color, HorizontalExpand = expand, ClipText = expand };
    }

    private static Label Heading(string key)
    {
        return new Label { Text = Loc.GetString(key), FontColorOverride = Color.FromHex("#c7d2e0"), Margin = new Thickness(0, 8, 0, 2) };
    }

    private static Button Btn(string key, Action act, bool enabled = true)
    {
        var button = new Button { Text = Loc.GetString(key), Disabled = !enabled };
        button.OnPressed += _ => act();
        return button;
    }

    /// <summary>A row of choices with the current one held down.</summary>
    private static BoxContainer Choices<T>(IEnumerable<T> values, T current, Func<T, string> label, Action<T> pick, bool enabled)
        where T : struct, Enum
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        foreach (var value in values)
        {
            var button = new Button
            {
                Text = label(value),
                ToggleMode = true,
                Pressed = EqualityComparer<T>.Default.Equals(value, current),
                Disabled = !enabled,
                MinWidth = 72,
            };
            button.OnPressed += _ => pick(value);
            row.AddChild(button);
        }

        return row;
    }

    private CheckBox Switch(WGShipConsoleState state, WGShipConsoleSwitch which)
    {
        var box = new CheckBox
        {
            Text = Loc.GetString($"wg-ship-console-switch-{which.ToString().ToLowerInvariant()}"),
            Pressed = state.Switches.GetValueOrDefault(which),
            Disabled = !state.CanConfigure,
        };
        box.OnToggled += args => Send(new WGShipConsoleSwitchMessage(which, args.Pressed));
        return box;
    }

    private static string Standing(WGShipAuthority authority)
    {
        return Loc.GetString($"wg-ship-console-standing-{authority.ToString().ToLowerInvariant()}");
    }

    #endregion

    public void UpdateState(WGShipConsoleState state)
    {
        _state = state;
        if (!state.Commanded)
        {
            _header.Text = Loc.GetString("wg-ship-console-not-commanded");
            _tabs.Visible = false;
            return;
        }

        _header.Text = Loc.GetString("wg-ship-console-header", ("ship", state.Ship), ("standing", Standing(state.Authority)),
            ("mode", Loc.GetString(state.CanConfigure ? "wg-ship-console-mode-configure" : "wg-ship-console-mode-view")));
        _tabs.Visible = state.CanView;
        if (!state.CanView)
        {
            _header.Text = Loc.GetString("wg-ship-console-not-cleared-header", ("ship", state.Ship));
            return;
        }

        TabContainer.SetTabVisible(_logPage, state.CanSeeLog);
        RebuildZones(state);
        RebuildPeople(state);
        RebuildPrices(state);
        RebuildCrew(state);
        RebuildOrders(state);
        RebuildJobs(state);
        RebuildPresets(state);
        RebuildPads(state);
        RebuildLog();
    }

    private void RebuildZones(WGShipConsoleState state)
    {
        _zones.RemoveAllChildren();
        var configure = state.CanConfigure;
        var modes = Enum.GetValues<WGShipZoneMode>();
        string ModeName(WGShipZoneMode mode) => Loc.GetString($"wg-ship-console-zone-{mode.ToString().ToLowerInvariant()}");

        if (state.Zones.Count == 0)
            _zones.AddChild(Text(Loc.GetString("wg-ship-console-no-zones"), Dim));

        foreach (var zone in state.Zones)
        {
            var row = HBox();
            row.AddChild(Text(zone.Name, expand: true));
            var members = new List<string>();
            if (zone.Doors > 0 || zone.Lockers == 0)
                members.Add(Loc.GetString("wg-ship-console-zone-doors", ("doors", zone.Doors)));
            if (zone.Lockers > 0)
                members.Add(Loc.GetString("wg-ship-console-zone-lockers", ("lockers", zone.Lockers)));
            row.AddChild(Text(string.Join(", ", members), Dim));
            row.AddChild(Choices(modes, zone.Mode, ModeName, mode => Send(new WGShipConsoleZoneMessage(zone.Id, mode)), configure));
            _zones.AddChild(row);
        }

        if (state.Zones.Count > 1)
        {
            var all = HBox();
            all.AddChild(Text(Loc.GetString("wg-ship-console-zone-all"), Dim, true));
            foreach (var mode in modes)
                all.AddChild(Btn($"wg-ship-console-zone-{mode.ToString().ToLowerInvariant()}", () => Send(new WGShipConsoleZoneMessage(null, mode)), configure));
            _zones.AddChild(all);
        }

        _zones.AddChild(Heading("wg-ship-console-responses"));
        foreach (var slot in Enum.GetValues<WGShipConsoleResponseSlot>())
        {
            var row = HBox();
            row.AddChild(Text(Loc.GetString($"wg-ship-console-response-{slot.ToString().ToLowerInvariant()}"), expand: true));
            var current = state.Responses.GetValueOrDefault(slot);
            row.AddChild(Choices(Enum.GetValues<WGCrewSecurityResponse>(), current,
                r => Loc.GetString($"wg-ship-console-answer-{r.ToString().ToLowerInvariant()}"),
                r => Send(new WGShipConsoleResponseMessage(slot, r)), configure));
            _zones.AddChild(row);
        }

        _zones.AddChild(Switch(state, WGShipConsoleSwitch.DocksWelcome));
    }

    private void RebuildPeople(WGShipConsoleState state)
    {
        _people.RemoveAllChildren();
        var configure = state.CanConfigure;
        foreach (var (list, people) in new[]
                 {
                     (WGShipCommandList.Officers, state.Officers),
                     (WGShipCommandList.Staff, state.Staff),
                     (WGShipCommandList.Barred, state.Barred),
                 })
        {
            _people.AddChild(Heading($"wg-ship-console-list-{list.ToString().ToLowerInvariant()}"));
            if (people.Count == 0)
                _people.AddChild(Text(Loc.GetString("wg-ship-console-nobody"), Dim));

            for (var i = 0; i < people.Count; i++)
            {
                var index = i;
                var person = people[i];
                var how = person.Card && person.Player ? "wg-ship-console-by-both" : person.Card ? "wg-ship-console-by-card" : "wg-ship-console-by-player";
                var row = HBox();
                row.AddChild(Text(person.Name, expand: true));
                row.AddChild(Text(Loc.GetString(how), Dim));
                var name = person.Name;
                row.AddChild(Btn("wg-ship-console-remove", () => Send(new WGShipConsoleRemovePersonMessage(list, index, name)), configure));
                _people.AddChild(row);
            }
        }

        _people.AddChild(Heading("wg-ship-console-aboard"));
        if (state.Aboard.Count == 0)
            _people.AddChild(Text(Loc.GetString("wg-ship-console-nobody"), Dim));

        foreach (var person in state.Aboard)
        {
            var row = HBox();
            row.AddChild(Text(person.Name, expand: true));
            row.AddChild(Text(Standing(person.Authority), Dim));
            foreach (var list in Enum.GetValues<WGShipCommandList>())
            {
                row.AddChild(Btn($"wg-ship-console-add-{list.ToString().ToLowerInvariant()}",
                    () => Send(new WGShipConsoleAddPersonMessage(person.Entity, list)),
                    configure && person.Authority is not (WGShipAuthority.Owner or WGShipAuthority.Admin)));
            }
            _people.AddChild(row);
        }

        _people.AddChild(Heading("wg-ship-console-admin-powers"));
        foreach (var which in new[]
                 {
                     WGShipConsoleSwitch.AdminsAtConsole, WGShipConsoleSwitch.AdminsInZones,
                     WGShipConsoleSwitch.AdminsNeverTargeted, WGShipConsoleSwitch.EventStaffAreAdmins,
                 })
            _people.AddChild(Switch(state, which));
    }

    private void RebuildPrices(WGShipConsoleState state)
    {
        _prices.RemoveAllChildren();
        var configure = state.CanConfigure;
        _ruleForm.Visible = configure;

        // Keep the chosen person while the people aboard change.
        var chosen = _rulePerson.SelectedId >= 0 && _rulePerson.SelectedId < _personChoices.Count
            ? _personChoices[_rulePerson.SelectedId].Entity
            : (NetEntity?) null;
        _personChoices = state.Aboard;
        _rulePerson.Clear();
        for (var i = 0; i < _personChoices.Count; i++)
        {
            _rulePerson.AddItem(_personChoices[i].Name, i);
            if (_personChoices[i].Entity == chosen)
                _rulePerson.SelectId(i);
        }

        _prices.AddChild(Heading("wg-ship-console-free"));
        _prices.AddChild(Switch(state, WGShipConsoleSwitch.OwnerServedFree));
        _prices.AddChild(Switch(state, WGShipConsoleSwitch.AdminsServedFree));

        _prices.AddChild(Heading("wg-ship-console-rules"));
        if (state.Rules.Count == 0)
            _prices.AddChild(Text(Loc.GetString("wg-ship-console-no-rules"), Dim));

        foreach (var rule in state.Rules)
        {
            var audience = Loc.GetString($"wg-ship-console-audience-{rule.Audience.ToString().ToLowerInvariant()}");
            if (rule.Who.Length > 0)
                audience += $": {rule.Who}";
            var services = rule.Services == WGShipServiceKind.All
                ? Loc.GetString("wg-ship-console-service-all")
                : string.Join(", ", Services.Where(s => rule.Services.HasFlag(s))
                    .Select(s => Loc.GetString($"wg-ship-console-service-{s.ToString().ToLowerInvariant()}")));
            var row = HBox();
            row.AddChild(Text(rule.Name, rule.Enabled ? null : Dim));
            row.AddChild(Text(Loc.GetString("wg-ship-console-rule-line", ("audience", audience), ("services", services),
                ("multiplier", rule.Multiplier.ToString("0.##", CultureInfo.InvariantCulture))), Dim, true));
            row.AddChild(Btn(rule.Enabled ? "wg-ship-console-rule-on" : "wg-ship-console-rule-off",
                () => Send(new WGShipConsoleToggleRuleMessage(rule.Name)), configure));
            row.AddChild(Btn("wg-ship-console-remove", () => Send(new WGShipConsoleRemoveRuleMessage(rule.Name)), configure));
            _prices.AddChild(row);
        }

        UpdateRuleForm();
    }

    private void UpdateRuleForm()
    {
        var audience = (WGShipPriceAudience) Math.Max(0, _ruleAudience.SelectedId);
        _rulePerson.Visible = audience == WGShipPriceAudience.Person;
        _ruleCompany.Visible = audience == WGShipPriceAudience.Company;
    }

    private void AddRule()
    {
        var audience = (WGShipPriceAudience) Math.Max(0, _ruleAudience.SelectedId);
        var services = WGShipServiceKind.None;
        foreach (var (kind, box) in _ruleServices)
        {
            if (box.Pressed)
                services |= kind;
        }

        var text = _ruleMultiplier.Text.Trim().Replace(',', '.');
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier))
            multiplier = -1;

        NetEntity? person = audience == WGShipPriceAudience.Person && _rulePerson.SelectedId >= 0 && _rulePerson.SelectedId < _personChoices.Count
            ? _personChoices[_rulePerson.SelectedId].Entity
            : null;
        Send(new WGShipConsoleSetRuleMessage(_ruleName.Text.Trim(), audience, person, _ruleCompany.Text.Trim(), services, multiplier));
    }

    private void RebuildOrders(WGShipConsoleState state)
    {
        _orders.RemoveAllChildren();
        _callsignForm.Visible = state.HasRoster && state.CanConfigure;
        if (!state.HasRoster)
        {
            _orders.AddChild(Text(Loc.GetString("wg-ship-console-no-roster"), Dim));
            return;
        }

        _orders.AddChild(Text(state.Callsign.Length > 0
            ? Loc.GetString("wg-ship-console-callsign-now", ("callsign", state.Callsign))
            : Loc.GetString("wg-ship-console-callsign-none"), Dim));
        OrderRow("marines", state.MarineOrders, state.CanConfigure, value => new WGShipConsoleOrdersMessage(value, null, null, null));
        OrderRow("gunners", state.GunnerPolicy, state.CanConfigure, value => new WGShipConsoleOrdersMessage(null, value, null, null));
        OrderRow("pilot", state.PilotStance, state.CanConfigure, value => new WGShipConsoleOrdersMessage(null, null, value, null));
    }

    /// <summary>One standing order: its heading, a button per choice, and what the current choice means.</summary>
    private void OrderRow<T>(string key, T current, bool configure, Func<T, BoundUserInterfaceMessage> message) where T : struct, Enum
    {
        string Name(T value) => Loc.GetString($"wg-ship-console-{key}-{value.ToString().ToLowerInvariant()}");

        _orders.AddChild(Heading($"wg-ship-console-{key}"));
        _orders.AddChild(Choices(Enum.GetValues<T>(), current, Name, value => Send(message(value)), configure));
        _orders.AddChild(Text(Loc.GetString($"wg-ship-console-{key}-{current.ToString().ToLowerInvariant()}-desc"), Dim));
    }

    private void RebuildCrew(WGShipConsoleState state)
    {
        _crew.RemoveAllChildren();
        var configure = state.CanConfigure;
        if (!state.HasRoster)
        {
            _crew.AddChild(Text(Loc.GetString("wg-ship-console-no-roster"), Dim));
            return;
        }

        _crew.AddChild(Switch(state, WGShipConsoleSwitch.AutoRestaff));
        var delay = state.RestaffDelay;
        var cap = state.RestaffsPerHour;
        _crew.AddChild(Row(
            Lbl("wg-ship-console-restaff-delay", ("seconds", delay)),
            Btn("wg-ship-console-less", () => Send(new WGShipConsoleRestaffMessage(delay - 30, cap)), configure && delay > 0),
            Btn("wg-ship-console-more", () => Send(new WGShipConsoleRestaffMessage(delay + 30, cap)), configure),
            Lbl("wg-ship-console-restaff-cap", ("count", cap)),
            Btn("wg-ship-console-less", () => Send(new WGShipConsoleRestaffMessage(delay, cap - 1)), configure && cap > 0),
            Btn("wg-ship-console-more", () => Send(new WGShipConsoleRestaffMessage(delay, cap + 1)), configure)));
        _crew.AddChild(Row(Btn("wg-ship-console-staff-all", () => Send(new WGShipConsoleStaffMessage(null)), configure)));

        foreach (var slot in state.Roster)
        {
            var row = HBox();
            row.AddChild(Text(slot.What, expand: true));
            row.AddChild(Text(Loc.GetString($"wg-ship-console-slot-{slot.State.ToString().ToLowerInvariant()}", ("who", slot.Who)),
                slot.State == WGShipConsoleSlotState.Empty ? Warn : Dim));
            row.AddChild(Btn("wg-ship-console-staff", () => Send(new WGShipConsoleStaffMessage(slot.Id)),
                configure && slot.State == WGShipConsoleSlotState.Empty));
            var down = slot.State != WGShipConsoleSlotState.StoodDown;
            row.AddChild(Btn(down ? "wg-ship-console-stand-down" : "wg-ship-console-stand-up",
                () => Send(new WGShipConsoleStandDownMessage(slot.Id, down)), configure && slot.State != WGShipConsoleSlotState.Player));
            _crew.AddChild(row);
        }
    }

    private void RebuildJobs(WGShipConsoleState state)
    {
        _jobs.RemoveAllChildren();
        var configure = state.CanConfigure;
        if (!state.HasJobs)
        {
            _jobs.AddChild(Text(Loc.GetString("wg-ship-console-no-jobs"), Dim));
            return;
        }

        foreach (var job in state.Jobs)
        {
            var row = HBox();
            row.AddChild(Text(job.Name, expand: true));
            row.AddChild(Text(Loc.GetString("wg-ship-console-job-open", ("count", job.Slots)), job.Slots > 0 ? Warn : Dim));
            row.AddChild(Btn("wg-ship-console-less", () => Send(new WGShipConsoleJobSlotsMessage(job.Id, job.Slots - 1)), configure && job.Slots > 0));
            row.AddChild(Btn("wg-ship-console-more", () => Send(new WGShipConsoleJobSlotsMessage(job.Id, job.Slots + 1)), configure));
            _jobs.AddChild(row);
            foreach (var holder in job.Holders)
            {
                var held = HBox();
                held.AddChild(Text(Loc.GetString("wg-ship-console-job-held", ("name", holder.Name)), Dim, true));
                held.AddChild(Btn("wg-ship-console-job-release", () => Send(new WGShipConsoleReleaseJobMessage(holder.Entity)), configure));
                _jobs.AddChild(held);
            }
        }
    }

    private void RebuildPresets(WGShipConsoleState state)
    {
        _presets.RemoveAllChildren();
        var configure = state.CanConfigure;
        _presetForm.Visible = configure && state.Vessel != null;
        if (state.Vessel == null)
        {
            _presets.AddChild(Text(Loc.GetString("wg-ship-console-no-vessel"), Dim));
            return;
        }

        _presets.AddChild(Text(Loc.GetString("wg-ship-console-presets-for", ("vessel", state.Vessel)), Dim));
        if (!configure)
            return;

        if (state.Presets.Count == 0)
            _presets.AddChild(Text(Loc.GetString("wg-ship-console-no-presets"), Dim));

        foreach (var preset in state.Presets)
        {
            var row = HBox();
            row.AddChild(Text(preset.Name, expand: true));
            row.AddChild(Text(preset.Saved.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), Dim));
            row.AddChild(Btn("wg-ship-console-preset-load", () => Send(new WGShipConsolePresetMessage(WGShipConsolePresetAction.Load, preset.Name))));
            var confirming = _confirmDelete == preset.Name;
            var delete = Btn(confirming ? "wg-ship-console-preset-delete-confirm" : "wg-ship-console-preset-delete", () =>
            {
                if (_confirmDelete != preset.Name)
                {
                    _confirmDelete = preset.Name;
                    if (_state != null)
                        RebuildPresets(_state);
                    return;
                }

                _confirmDelete = null;
                Send(new WGShipConsolePresetMessage(WGShipConsolePresetAction.Delete, preset.Name));
            });
            if (confirming)
                delete.ModulateSelfOverride = Warn;
            row.AddChild(delete);
            _presets.AddChild(row);
        }
    }

    private void RebuildPads(WGShipConsoleState state)
    {
        _pads.RemoveAllChildren();
        if (state.Pads.Count == 0)
        {
            _pads.AddChild(Text(Loc.GetString("wg-ship-console-no-pads"), Dim));
            return;
        }

        foreach (var pad in state.Pads)
        {
            var row = HBox();
            row.AddChild(Text(pad.Name, expand: true));
            row.AddChild(Choices(PadLevels, pad.Access,
                level => Loc.GetString($"wg-ship-console-pad-{level.ToString().ToLowerInvariant()}"),
                level => Send(new WGShipConsolePadAccessMessage(pad.Entity, level)), state.CanConfigure));
            _pads.AddChild(row);
        }
    }

    private void RebuildLog()
    {
        _log.RemoveAllChildren();
        if (_state is not { CanSeeLog: true } state)
            return;

        var filter = _logFilter.Text.Trim();
        var shown = 0;
        foreach (var entry in state.Log)
        {
            var line = Loc.GetString("wg-ship-console-log-line",
                ("time", entry.Time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)),
                ("customer", entry.Customer),
                ("kind", Loc.GetString($"wg-ship-console-service-{entry.Kind.ToString().ToLowerInvariant()}")),
                ("service", entry.Service),
                ("what", entry.What),
                ("charged", entry.Charged),
                ("base", entry.BasePrice),
                ("rule", entry.Rule ?? "-"));
            if (filter.Length > 0 && !line.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            _log.AddChild(Text(line, Dim));
            shown++;
        }

        if (shown == 0)
            _log.AddChild(Text(Loc.GetString("wg-ship-console-log-empty"), Dim));
    }
}
