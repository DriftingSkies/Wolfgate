using System.Linq;
using System.Numerics;
using Content.Client.Atmos.Monitor.UI.Widgets;
using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.MappingMode.Link;

/// <summary>
/// The alarm thresholds of an atmos monitor, or of every monitor an air alarm lists, with the air alarm's own
/// threshold controls and a preset picker per kind. Apply sets the target, Apply to selection every monitor and air
/// alarm in the mapping tools' selection.
/// </summary>
public sealed class WFMappingThresholdWindow : DefaultWindow
{
    private readonly IPrototypeManager _prototypes;
    private readonly Label _targetLabel;
    private readonly BoxContainer _rows;
    private readonly List<string> _presets;
    private MappingThresholdSet _thresholds = new();

    /// <summary>The monitor or air alarm the window was opened on.</summary>
    public NetEntity? Target { get; private set; }

    /// <summary>Raised with the edited thresholds for the target.</summary>
    public event Action<MappingThresholdSet>? Applied;

    /// <summary>Raised with the edited thresholds for everything in the selection.</summary>
    public event Action<MappingThresholdSet>? AppliedToSelection;

    public WFMappingThresholdWindow(IPrototypeManager prototypes)
    {
        _prototypes = prototypes;
        _presets = prototypes.EnumeratePrototypes<AtmosAlarmThresholdPrototype>()
            .Select(p => p.ID)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Title = Loc.GetString("wf-mapping-link-thresholds-title");
        MinSize = new Vector2(480, 300);
        SetSize = new Vector2(620, 640);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true,
        };

        _targetLabel = new Label { ClipText = true };
        root.AddChild(_targetLabel);
        root.AddChild(new Label
        {
            Text = Loc.GetString("wf-mapping-link-thresholds-note"),
            StyleClasses = { "LabelSubText" },
        });

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _rows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        scroll.AddChild(_rows);
        root.AddChild(scroll);

        var footer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        var applyButton = new Button { Text = Loc.GetString("wf-mapping-link-apply"), HorizontalExpand = true };
        var selectionButton = new Button
        {
            Text = Loc.GetString("wf-mapping-link-access-selection"),
            ToolTip = Loc.GetString("wf-mapping-link-thresholds-selection-tooltip"),
            HorizontalExpand = true,
        };
        footer.AddChild(applyButton);
        footer.AddChild(selectionButton);
        root.AddChild(footer);
        Contents.AddChild(root);

        applyButton.OnPressed += _ => Applied?.Invoke(_thresholds.Clone());
        selectionButton.OnPressed += _ => AppliedToSelection?.Invoke(_thresholds.Clone());
    }

    /// <summary>Shows a target's thresholds; <paramref name="monitors"/> is how many an Apply sets.</summary>
    public void SetTarget(NetEntity target, string name, int monitors, MappingThresholdSet thresholds)
    {
        Target = target;
        _thresholds = thresholds.Clone();
        _targetLabel.Text = Loc.GetString("wf-mapping-link-thresholds-target", ("name", name), ("count", monitors));
        Rebuild();
    }

    private void Rebuild()
    {
        _rows.RemoveAllChildren();

        if (_thresholds.Temperature != null)
        {
            AddRow(Loc.GetString("air-alarm-ui-thresholds-temperature-title"), _thresholds.Temperature,
                AtmosMonitorThresholdType.Temperature, null, 1,
                Loc.GetString("wf-mapping-link-thresholds-kelvin"),
                proto => _thresholds.Temperature = proto);
        }

        if (_thresholds.Pressure != null)
        {
            AddRow(Loc.GetString("air-alarm-ui-thresholds-pressure-title"), _thresholds.Pressure,
                AtmosMonitorThresholdType.Pressure, null, 1, null,
                proto => _thresholds.Pressure = proto);
        }

        foreach (var gas in _thresholds.Gases.Keys.OrderBy(g => g).ToList())
        {
            AddRow(Loc.GetString("air-alarm-ui-thresholds-gas-title", ("gas", $"{gas}")), _thresholds.Gases[gas],
                AtmosMonitorThresholdType.Gas, gas, 100, null,
                proto => _thresholds.Gases[gas] = proto);
        }

        if (_rows.ChildCount == 0)
            _rows.AddChild(new Label { Text = Loc.GetString("wf-mapping-link-thresholds-none") });
    }

    /// <summary>A preset picker over the game's threshold control, which edits <paramref name="threshold"/> in place.</summary>
    private void AddRow(string title, AtmosAlarmThreshold threshold, AtmosMonitorThresholdType type, Gas? gas,
        float modifier, string? hint, Action<AtmosAlarmThreshold> replace)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var presetRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        presetRow.AddChild(new Label { Text = Loc.GetString("wf-mapping-link-thresholds-preset", ("kind", title)) });

        var picker = new OptionButton { HorizontalExpand = true };
        picker.AddItem(Loc.GetString("wf-mapping-link-thresholds-preset-pick"), 0);
        for (var i = 0; i < _presets.Count; i++)
        {
            picker.AddItem(_presets[i], i + 1);
        }

        picker.OnItemSelected += args =>
        {
            if (args.Id == 0 || !_prototypes.TryIndex<AtmosAlarmThresholdPrototype>(_presets[args.Id - 1], out var proto))
                return;

            replace(new AtmosAlarmThreshold(proto));
            Rebuild();
        };
        presetRow.AddChild(picker);
        row.AddChild(presetRow);

        if (hint != null)
            row.AddChild(new Label { Text = hint, StyleClasses = { "LabelSubText" } });

        var control = new ThresholdControl(title, threshold, type, gas, modifier);
        control.ThresholdDataChanged += (_, _, _) => { };
        row.AddChild(control);
        _rows.AddChild(row);
    }
}
