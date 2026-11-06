using System.Globalization;
using System.Linq;
using Content.Shared._WF.Warheads;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.Warheads;

/// <summary>
/// Admin window for building bespoke warheads: start from a payload preset, tune every field, pick a chassis and
/// fuzes, then spawn it or set the payload off on the spot.
/// </summary>
public sealed class WFNukeDesignerWindow : DefaultWindow
{
    public event Action<WFNukeDesign>? OnSpawn;
    public event Action<WFWarheadPayload>? OnDetonate;

    private readonly OptionButton _presets = new() { HorizontalExpand = true };
    private readonly OptionButton _chassis = new() { HorizontalExpand = true };
    private readonly OptionButton _explosion = new() { HorizontalExpand = true };
    private readonly LineEdit _name = new() { HorizontalExpand = true };
    private readonly LineEdit _timer = new() { HorizontalExpand = true, Text = "120" };
    private readonly CheckBox _timerFuze = new() { Pressed = true };
    private readonly CheckBox _twoPerson = new();
    private readonly CheckBox _remote = new();
    private readonly CheckBox _impact = new();
    private readonly CheckBox _startNow = new();
    private readonly Label _status = new() { HorizontalExpand = true };
    private readonly GridContainer _fields = new() { Columns = 2, HorizontalExpand = true };

    private readonly List<string> _presetIds = new();
    private readonly List<string> _chassisIds = new();
    private readonly List<string?> _explosionIds = new();
    private readonly List<Field> _fieldList = new();
    private Dictionary<string, WFWarheadPayload> _presetPayloads = new();
    private WFWarheadPayload _payload = new();

    public WFNukeDesignerWindow()
    {
        Title = Loc.GetString("wf-nuke-designer-title");
        MinSize = new(560, 680);

        _presets.OnItemSelected += args =>
        {
            _presets.SelectId(args.Id);
            if (_presetPayloads.TryGetValue(_presetIds[args.Id], out var preset))
                LoadPayload(preset.Clone());
        };
        _chassis.OnItemSelected += args => _chassis.SelectId(args.Id);
        _explosion.OnItemSelected += args =>
        {
            _explosion.SelectId(args.Id);
            _payload.ExplosionType = _explosionIds[args.Id];
        };

        var spawn = new Button { Text = Loc.GetString("wf-nuke-designer-spawn"), HorizontalExpand = true };
        spawn.OnPressed += _ => Spawn();
        var detonate = new Button { Text = Loc.GetString("wf-nuke-designer-detonate"), HorizontalExpand = true };
        detonate.StyleClasses.Add("Caution");
        detonate.OnPressed += _ =>
        {
            if (ReadFields())
                OnDetonate?.Invoke(_payload.Clone());
        };

        BuildFields();

        var fuzes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        AddCheck(fuzes, _timerFuze, "wf-nuke-designer-fuze-timer");
        AddCheck(fuzes, _twoPerson, "wf-nuke-designer-fuze-two-person");
        AddCheck(fuzes, _remote, "wf-nuke-designer-fuze-remote");
        AddCheck(fuzes, _impact, "wf-nuke-designer-fuze-impact");

        var startRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        AddCheck(startRow, _startNow, "wf-nuke-designer-start-now");

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Children =
            {
                Row("wf-nuke-designer-preset", _presets),
                Row("wf-nuke-designer-chassis", _chassis),
                Row("wf-nuke-designer-name", _name),
                Row("wf-nuke-designer-timer", _timer),
                new Label { Text = Loc.GetString("wf-nuke-designer-fuzes"), StyleClasses = { "LabelSubText" } },
                fuzes,
                startRow,
                new Label { Text = Loc.GetString("wf-nuke-designer-payload"), StyleClasses = { "LabelSubText" } },
                Row("wf-nuke-designer-field-explosion-type", _explosion),
                new ScrollContainer
                {
                    VerticalExpand = true,
                    HScrollEnabled = false,
                    Children = { _fields },
                },
                _status,
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 6,
                    Children = { spawn, detonate },
                },
            },
        });
    }

    private static Control Row(string label, Control control)
    {
        return new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Children =
            {
                new Label { Text = Loc.GetString(label), MinWidth = 150 },
                control,
            },
        };
    }

    private static void AddCheck(BoxContainer box, CheckBox check, string label)
    {
        check.Text = Loc.GetString(label);
        box.AddChild(check);
    }

    /// <summary>One editable payload field: its label and how to read and write it.</summary>
    private sealed record Field(string Key, LineEdit Edit, Func<WFWarheadPayload, string> Get, Func<WFWarheadPayload, string, bool> Set);

    private void AddFloat(string key, Func<WFWarheadPayload, float> get, Action<WFWarheadPayload, float> set)
    {
        AddField(key,
            p => get(p).ToString(CultureInfo.InvariantCulture),
            (p, text) =>
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                    return false;
                set(p, value);
                return true;
            });
    }

    private void AddField(string key, Func<WFWarheadPayload, string> get, Func<WFWarheadPayload, string, bool> set)
    {
        var edit = new LineEdit { HorizontalExpand = true };
        var label = new Label { Text = Loc.GetString($"wf-nuke-designer-field-{key}"), MinWidth = 220 };
        label.ToolTip = Loc.GetString($"wf-nuke-designer-field-{key}-tooltip");
        _fields.AddChild(label);
        _fields.AddChild(edit);
        _fieldList.Add(new Field(key, edit, get, set));
    }

    private void BuildFields()
    {
        AddFloat("total-intensity", p => p.TotalIntensity, (p, v) => p.TotalIntensity = Math.Max(v, 0f));
        AddFloat("intensity-slope", p => p.IntensitySlope, (p, v) => p.IntensitySlope = Math.Max(v, 0.01f));
        AddFloat("max-intensity", p => p.MaxIntensity, (p, v) => p.MaxIntensity = Math.Max(v, 0f));
        AddFloat("tile-break-scale", p => p.TileBreakScale, (p, v) => p.TileBreakScale = Math.Max(v, 0f));
        AddField("delete-grid", p => p.DeleteGrid.ToString(), (p, t) => bool.TryParse(t, out var b) && Assign(() => p.DeleteGrid = b));
        AddFloat("erase-radius", p => p.EraseRadius, (p, v) => p.EraseRadius = Math.Clamp(v, 0f, 200f));
        AddFloat("prompt-radiation", p => p.PromptRadiation, (p, v) => p.PromptRadiation = Math.Max(v, 0f));
        AddFloat("prompt-radiation-slope", p => p.PromptRadiationSlope, (p, v) => p.PromptRadiationSlope = Math.Max(v, 0.001f));
        AddFloat("prompt-radiation-duration", p => p.PromptRadiationDuration, (p, v) => p.PromptRadiationDuration = Math.Max(v, 0.1f));
        AddField("fallout-prototype", p => p.FalloutPrototype, (p, t) => !string.IsNullOrWhiteSpace(t) && Assign(() => p.FalloutPrototype = t.Trim()));
        AddField("fallout-count", p => p.FalloutCount.ToString(CultureInfo.InvariantCulture),
            (p, t) => int.TryParse(t, out var n) && Assign(() => p.FalloutCount = Math.Clamp(n, 0, 64)));
        AddFloat("fallout-spread", p => p.FalloutSpread, (p, v) => p.FalloutSpread = Math.Max(v, 0f));
        AddFloat("fallout-intensity", p => p.FalloutIntensity, (p, v) => p.FalloutIntensity = Math.Max(v, 0f));
        AddFloat("fallout-half-life", p => p.FalloutHalfLife, (p, v) => p.FalloutHalfLife = Math.Max(v, 1f));
        AddFloat("fallout-radius", p => p.FalloutRadius, (p, v) => p.FalloutRadius = Math.Max(v, 0.5f));
        AddFloat("emp-range", p => p.EmpRange, (p, v) => p.EmpRange = Math.Max(v, 0f));
        AddFloat("emp-energy", p => p.EmpEnergy, (p, v) => p.EmpEnergy = Math.Max(v, 0f));
        AddFloat("emp-duration", p => p.EmpDuration, (p, v) => p.EmpDuration = Math.Max(v, 0f));
        AddFloat("flash-range", p => p.FlashRange, (p, v) => p.FlashRange = Math.Max(v, 0f));
        AddFloat("fireball-radius", p => p.FireballRadius, (p, v) => p.FireballRadius = Math.Max(v, 0.1f));
        AddFloat("shockwave-range", p => p.ShockwaveRange, (p, v) => p.ShockwaveRange = Math.Max(v, 0f));
        AddFloat("shockwave-speed", p => p.ShockwaveSpeed, (p, v) => p.ShockwaveSpeed = Math.Max(v, 1f));
        AddFloat("shake-strength", p => p.ShakeStrength, (p, v) => p.ShakeStrength = Math.Max(v, 0f));
        AddFloat("effect-duration", p => p.EffectDuration, (p, v) => p.EffectDuration = Math.Max(v, 0.5f));
        AddField("map-wide-audio", p => p.MapWideAudio.ToString(), (p, t) => bool.TryParse(t, out var b) && Assign(() => p.MapWideAudio = b));
        AddField("detonation-sound", p => p.DetonationSound ?? string.Empty,
            (p, t) => Assign(() => p.DetonationSound = string.IsNullOrWhiteSpace(t) ? null : t.Trim()));
    }

    private static bool Assign(Action set)
    {
        set();
        return true;
    }

    public void Update(WFNukeDesignerEuiState state)
    {
        _status.Text = state.Status ?? string.Empty;

        if (_presetIds.Count == 0)
        {
            _presetPayloads = state.Presets;
            foreach (var id in state.Presets.Keys.OrderBy(k => k))
            {
                _presets.AddItem(id, _presetIds.Count);
                _presetIds.Add(id);
            }

            foreach (var (id, name) in state.Chassis.OrderBy(c => c.Value))
            {
                _chassis.AddItem($"{name} ({id})", _chassisIds.Count);
                _chassisIds.Add(id);
            }

            _explosion.AddItem(Loc.GetString("wf-nuke-designer-explosion-none"), 0);
            _explosionIds.Add(null);
            foreach (var id in state.ExplosionTypes)
            {
                _explosion.AddItem(id, _explosionIds.Count);
                _explosionIds.Add(id);
            }

            if (_presetIds.Count > 0)
            {
                _presets.SelectId(0);
                LoadPayload(state.Presets[_presetIds[0]].Clone());
            }
        }
    }

    private void LoadPayload(WFWarheadPayload payload)
    {
        _payload = payload;
        foreach (var field in _fieldList)
        {
            field.Edit.Text = field.Get(payload);
        }

        var index = _explosionIds.IndexOf(payload.ExplosionType);
        _explosion.SelectId(Math.Max(index, 0));
    }

    /// <summary>Reads every field into the payload; marks and reports the first bad one.</summary>
    private bool ReadFields()
    {
        foreach (var field in _fieldList)
        {
            if (field.Set(_payload, field.Edit.Text))
                continue;

            _status.Text = Loc.GetString("wf-nuke-designer-status-bad-field",
                ("field", Loc.GetString($"wf-nuke-designer-field-{field.Key}")));
            field.Edit.GrabKeyboardFocus();
            return false;
        }

        return true;
    }

    private void Spawn()
    {
        if (!ReadFields())
            return;

        if (_chassis.SelectedId < 0 || _chassis.SelectedId >= _chassisIds.Count)
        {
            _status.Text = Loc.GetString("wf-nuke-designer-status-bad-chassis");
            return;
        }

        if (!float.TryParse(_timer.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var timer) || timer < 0f)
        {
            _status.Text = Loc.GetString("wf-nuke-designer-status-bad-field", ("field", Loc.GetString("wf-nuke-designer-timer")));
            return;
        }

        var fuzes = WFWarheadFuzes.None;
        if (_timerFuze.Pressed)
            fuzes |= WFWarheadFuzes.Timer;
        if (_twoPerson.Pressed)
            fuzes |= WFWarheadFuzes.TwoPerson;
        if (_remote.Pressed)
            fuzes |= WFWarheadFuzes.Remote;
        if (_impact.Pressed)
            fuzes |= WFWarheadFuzes.Impact;

        OnSpawn?.Invoke(new WFNukeDesign
        {
            Chassis = _chassisIds[_chassis.SelectedId],
            Payload = _payload.Clone(),
            Fuzes = fuzes,
            Timer = timer,
            Name = string.IsNullOrWhiteSpace(_name.Text) ? null : _name.Text,
            StartCountdown = _startNow.Pressed,
        });
    }
}
