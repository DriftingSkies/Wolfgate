using System.Globalization;
using System.Numerics;
using System.Text;
using Content.Client._WF.NpcCrew;
using Content.Shared._WF.Encounters;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.Encounters;

/// <summary>
/// The admin encounter window: scheduler settings, starting any encounter, and every running encounter with its
/// ships, each of which opens in the crew setup window for full control.
/// </summary>
[UsedImplicitly]
public sealed partial class WFEncounterWindow : DefaultWindow
{
    [Dependency] private IEntityManager _entities = default!;

    private readonly WFEncounterClientSystem _system;
    private readonly CheckBox _enabled = new();
    private readonly CheckBox _paused = new();
    private readonly LineEdit _intervalMin = new() { MinWidth = 70 };
    private readonly LineEdit _intervalMax = new() { MinWidth = 70 };
    private readonly LineEdit _maxActive = new() { MinWidth = 70 };
    private readonly Label _next = new();
    private readonly OptionButton _preset = new();
    private readonly Label _budget = new();
    private readonly List<string> _presetIds = new();
    private readonly OptionButton _prototype = new() { HorizontalExpand = true };
    private readonly LineEdit _distance = new() { Text = "300", MinWidth = 70 };
    private readonly LineEdit _search = new() { HorizontalExpand = true };
    private readonly CheckBox _keep = new() { Pressed = true };
    private readonly Label _status = new();
    private readonly BoxContainer _list = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
    private readonly List<string> _prototypeIds = new();
    private readonly List<WFEncounterAdminPrototype> _prototypes = new();
    private WFEncounterAdminState? _last;
    private bool _schedulerLoaded;
    private string _shown = string.Empty;

    public WFEncounterWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("wf-encounter-admin-title");
        MinSize = new Vector2(620, 420);
        SetSize = new Vector2(760, 620);
        _system = _entities.System<WFEncounterClientSystem>();

        var body = Column(10);
        Contents.AddChild(body);

        body.AddChild(Heading("scheduler"));
        _enabled.Text = Text("enabled");
        _paused.Text = Text("paused");
        body.AddChild(Row(_enabled, _paused, _next));
        body.AddChild(Row(new Label { Text = Text("interval") }, _intervalMin, new Label { Text = Text("to") }, _intervalMax,
            new Label { Text = Text("cap") }, _maxActive));
        body.AddChild(Row(Button("apply", ApplyScheduler), Button("schedule-now", () => Send(WFEncounterAdminAction.Schedule)),
            Button("start-round", () => Send(WFEncounterAdminAction.StartRound))));
        _preset.OnItemSelected += args =>
        {
            _preset.SelectId(args.Id);
            _system.Send(new WFEncounterAdminRequest { Action = WFEncounterAdminAction.Preset, Prototype = _presetIds[args.Id] });
        };
        body.AddChild(Row(new Label { Text = Text("preset") }, _preset, _budget));

        body.AddChild(Heading("start"));
        _search.PlaceHolder = Text("search-hint");
        _search.OnTextChanged += _ => Filter();
        body.AddChild(Row(new Label { Text = Text("search") }, _search));
        _prototype.OnItemSelected += args => _prototype.SelectId(args.Id);
        _keep.Text = Text("keep");
        body.AddChild(Row(_prototype, new Label { Text = Text("distance") }, _distance, _keep, Button("spawn", Spawn)));

        body.AddChild(Heading("running"));
        body.AddChild(_status);
        body.AddChild(new ScrollContainer
        {
            VerticalExpand = true,
            HScrollEnabled = false,
            Children = { _list },
        });

        _system.Received += Receive;
        OnClose += () => _system.Received -= Receive;
        Send(WFEncounterAdminAction.Refresh);
    }

    private static string Text(string key, params (string, object)[] args)
    {
        return Loc.GetString($"wf-encounter-admin-{key}", args);
    }

    private static BoxContainer Column(int separation)
    {
        return new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = separation };
    }

    private static BoxContainer Row(params Control[] controls)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        foreach (var control in controls)
        {
            row.AddChild(control);
        }

        return row;
    }

    private static Label Heading(string key)
    {
        return new Label { Text = Text(key), StyleClasses = { "LabelHeading" } };
    }

    private static Button Button(string key, Action pressed)
    {
        var button = new Button { Text = Text(key) };
        button.OnPressed += _ => pressed();
        return button;
    }

    private void Send(WFEncounterAdminAction action, NetEntity? target = null)
    {
        _system.Send(new WFEncounterAdminRequest { Action = action, Target = target });
    }

    private void Spawn()
    {
        if (_prototypeIds.Count == 0 || !TryNumber(_distance.Text, out var distance))
        {
            _status.Text = Text("bad-input");
            return;
        }

        _system.Send(new WFEncounterAdminRequest
        {
            Action = WFEncounterAdminAction.Spawn,
            Prototype = _prototypeIds[_prototype.SelectedId],
            Distance = distance,
            Pinned = _keep.Pressed,
        });
    }

    private void ApplyScheduler()
    {
        if (!TryNumber(_intervalMin.Text, out var min) || !TryNumber(_intervalMax.Text, out var max)
            || !int.TryParse(_maxActive.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap))
        {
            _status.Text = Text("bad-input");
            return;
        }

        _schedulerLoaded = false;
        _system.Send(new WFEncounterAdminRequest
        {
            Action = WFEncounterAdminAction.Scheduler,
            Enabled = _enabled.Pressed,
            Paused = _paused.Pressed,
            IntervalMin = min,
            IntervalMax = max,
            MaxActive = cap,
        });
    }

    private static bool TryNumber(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    }

    private void Receive(WFEncounterAdminState state)
    {
        // Settings are only loaded once and after Apply, so a poll never overwrites what the admin is typing.
        if (!_schedulerLoaded)
        {
            _schedulerLoaded = true;
            _enabled.Pressed = state.Enabled;
            _paused.Pressed = state.Paused;
            _intervalMin.Text = state.IntervalMin.ToString("0", CultureInfo.InvariantCulture);
            _intervalMax.Text = state.IntervalMax.ToString("0", CultureInfo.InvariantCulture);
            _maxActive.Text = state.MaxActive.ToString(CultureInfo.InvariantCulture);
        }

        _next.Text = state.NextIn >= 0f ? Text("next", ("time", Duration(state.NextIn))) : Text("next-none");
        _budget.Text = Text("budget", ("cost", state.Cost), ("budget", state.Budget));
        if (_presetIds.Count != state.Presets.Count)
        {
            _presetIds.Clear();
            _preset.Clear();
            foreach (var preset in state.Presets)
            {
                _preset.AddItem(preset.Name, _presetIds.Count);
                _presetIds.Add(preset.Id);
            }
        }

        var current = _presetIds.IndexOf(state.Preset);
        if (current >= 0 && _preset.SelectedId != current)
            _preset.SelectId(current);
        _last = state;
        if (_prototypes.Count != state.Prototypes.Count)
        {
            _prototypes.Clear();
            _prototypes.AddRange(state.Prototypes);
            FillPrototypes();
        }

        ShowEncounters(state);
    }

    /// <summary>Applies the search box to both lists: the encounters that can be started and the ones running.</summary>
    private void Filter()
    {
        FillPrototypes();
        if (_last != null)
            ShowEncounters(_last);
    }

    /// <summary>Whether a piece of text is what the search box asks for. An empty box matches everything.</summary>
    private bool Matches(params string[] texts)
    {
        var needle = _search.Text.Trim();
        if (needle.Length == 0)
            return true;

        foreach (var text in texts)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Refills the start list with the prototypes the search box lets through, keeping the one chosen where it can.</summary>
    private void FillPrototypes()
    {
        var chosen = _prototypeIds.Count > 0 && _prototype.SelectedId < _prototypeIds.Count ? _prototypeIds[_prototype.SelectedId] : null;
        _prototypeIds.Clear();
        _prototype.Clear();
        foreach (var prototype in _prototypes)
        {
            if (!Matches(prototype.Id))
                continue;

            _prototype.AddItem(prototype.Scheduled ? prototype.Id : Text("prototype-manual", ("id", prototype.Id)), _prototypeIds.Count);
            _prototypeIds.Add(prototype.Id);
        }

        var keep = chosen != null ? _prototypeIds.IndexOf(chosen) : -1;
        if (keep >= 0)
            _prototype.SelectId(keep);
    }

    private void ShowEncounters(WFEncounterAdminState state)
    {
        var shown = new List<WFEncounterAdminEntry>();
        foreach (var encounter in state.Encounters)
        {
            if (Matches(encounter.Name, encounter.Prototype, encounter.State) || encounter.Ships.Exists(ship => Matches(ship.Key, ship.Name, ship.Side)))
                shown.Add(encounter);
        }

        if (state.Message.Length > 0)
            _status.Text = state.Message;
        else if (state.Encounters.Count == 0)
            _status.Text = Text("none");
        else if (shown.Count == 0)
            _status.Text = Text("none-match");
        else
            _status.Text = string.Empty;

        // Rebuilding the rows under the cursor would eat clicks, so only do it when something shown changed.
        var signature = Signature(shown);
        if (signature == _shown)
            return;

        _shown = signature;
        _list.RemoveAllChildren();
        foreach (var encounter in shown)
        {
            _list.AddChild(Entry(encounter));
        }
    }

    private static string Signature(List<WFEncounterAdminEntry> encounters)
    {
        var text = new StringBuilder();
        foreach (var encounter in encounters)
        {
            text.Append(encounter.Uid).Append(encounter.State).Append(encounter.Hidden).Append(encounter.Pinned).Append((int) (encounter.Age / 60f)).Append((int) (encounter.ExpiresIn / 60f));
            foreach (var ship in encounter.Ships)
            {
                text.Append(ship.Grid).Append(ship.Exists).Append(ship.Disabled).Append(ship.Crew).Append(ship.Activity);
            }
        }

        return text.ToString();
    }

    private Control Entry(WFEncounterAdminEntry encounter)
    {
        var box = Column(4);
        var uid = encounter.Uid;
        var title = new Label
        {
            Text = Text(encounter.Pinned ? "entry-pinned" : "entry", ("name", encounter.Name), ("prototype", encounter.Prototype),
                ("state", encounter.State), ("age", Duration(encounter.Age))),
            HorizontalExpand = true,
            ClipText = true,
        };
        var header = Row(title, Button("teleport", () => Send(WFEncounterAdminAction.Teleport, uid)));
        if (encounter.Hidden && !encounter.Resolved)
            header.AddChild(Button("reveal", () => Send(WFEncounterAdminAction.Reveal, uid)));
        if (!encounter.Resolved)
            header.AddChild(Button("resolve", () => Send(WFEncounterAdminAction.Resolve, uid)));
        header.AddChild(Button("end", () => Send(WFEncounterAdminAction.End, uid)));
        box.AddChild(header);
        if (encounter.ExpiresIn >= 0f && !encounter.Resolved)
            box.AddChild(new Label { Text = Text("expires", ("time", Duration(encounter.ExpiresIn))) });

        foreach (var ship in encounter.Ships)
        {
            var grid = ship.Grid;
            var group = ship.Group;
            var label = new Label
            {
                Text = ship.Exists
                    ? Text(ship.Disabled ? "ship-disabled" : "ship", ("key", ship.Key), ("name", ship.Name), ("side", ship.Side),
                        ("crew", ship.Crew), ("activity", ship.Activity))
                    : Text("ship-gone", ("key", ship.Key), ("side", ship.Side)),
                HorizontalExpand = true,
                ClipText = true,
                Margin = new Thickness(16, 0, 0, 0),
            };
            var row = Row(label);
            if (ship.Exists)
            {
                row.AddChild(Button("teleport", () => Send(WFEncounterAdminAction.Teleport, grid)));
                row.AddChild(Button("crew", () =>
                {
                    var window = new WFCrewSetupWindow();
                    window.SelectCrew(grid, group);
                    window.OpenCentered();
                }));
            }

            box.AddChild(row);
        }

        return box;
    }

    private static string Duration(float seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
