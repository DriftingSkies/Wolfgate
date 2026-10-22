using Content.Client._WF.MappingMode.Modes;
using Content.Shared._WF.MappingMode;
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._WF.MappingMode.Panels;

/// <summary>The mapping screen's keys: the mode keys with their bindings, each mode's mouse actions and the tools' keys.</summary>
public sealed partial class WFMappingKeysPanel : BoxContainer
{
    [Dependency] private IInputManager _input = default!;

    private static readonly BoundKeyFunction[] ToolKeys =
    {
        ContentKeyFunctions.WFMappingEnableSelect,
        ContentKeyFunctions.WFMappingRotate,
        ContentKeyFunctions.WFMappingMirror,
        ContentKeyFunctions.WFMappingMirrorVertical,
        ContentKeyFunctions.WFMappingDelete,
        ContentKeyFunctions.WFMappingCopy,
        ContentKeyFunctions.WFMappingCut,
        ContentKeyFunctions.WFMappingPaste,
        ContentKeyFunctions.WFMappingUndo,
        ContentKeyFunctions.WFMappingRedo,
        ContentKeyFunctions.WFMappingLevelUp,
        ContentKeyFunctions.WFMappingLevelDown,
        WFMappingKeyFunctions.WFMappingModeCancel,
        WFMappingKeyFunctions.WFMappingLinkCycle,
        WFMappingKeyFunctions.WFMappingQuicksave,
    };

    private readonly GridContainer _grid;

    public WFMappingKeysPanel()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        var guide = new Button
        {
            Text = Loc.GetString("wf-mapping-guide-button"),
            ToolTip = Loc.GetString("wf-mapping-guide-button-tooltip"),
            HorizontalAlignment = HAlignment.Left,
            Margin = new Thickness(6, 6, 6, 0),
        };
        guide.OnPressed += _ => WFMappingGuideLauncher.Open();
        AddChild(guide);

        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
        _grid = new GridContainer { Columns = 2, HSeparationOverride = 12, VSeparationOverride = 2, Margin = new Thickness(6) };
        scroll.AddChild(_grid);
        AddChild(scroll);
        Refresh();
    }

    /// <summary>Rebuilds the list with the current bindings.</summary>
    public void Refresh()
    {
        _grid.RemoveAllChildren();

        Heading("wf-mapping-screen-keys-modes");
        for (var i = 0; i < WFMappingKeyFunctions.Modes.Length; i++)
        {
            var mode = ((WFMappingBuildMode) (i + 1)).ToString().ToLowerInvariant();
            Row(Bound(WFMappingKeyFunctions.Modes[i]), Loc.GetString($"wf-mapping-screen-mode-{mode}"));
        }

        Heading("wf-mapping-screen-keys-mouse");
        foreach (var mode in Enum.GetValues<WFMappingBuildMode>())
        {
            var name = mode.ToString().ToLowerInvariant();
            Row(Loc.GetString($"wf-mapping-screen-mode-{name}"), Loc.GetString($"wf-mapping-screen-mouse-{name}"));
        }

        Row(Loc.GetString("wf-mapping-screen-keys-ctrl-wheel"), Loc.GetString("wf-mapping-screen-keys-ctrl-wheel-desc"));

        Heading("wf-mapping-screen-keys-tools");
        foreach (var function in ToolKeys)
        {
            Row(Bound(function), Loc.GetString($"wf-mapping-screen-key-{function.FunctionName}"));
        }
    }

    private string Bound(BoundKeyFunction function)
    {
        var text = _input.GetKeyFunctionButtonString(function);
        return string.IsNullOrEmpty(text) ? Loc.GetString("wf-mapping-screen-key-unbound") : text;
    }

    private void Heading(string key)
    {
        _grid.AddChild(new Label { Text = Loc.GetString(key), StyleClasses = { "LabelHeading" } });
        _grid.AddChild(new Control());
    }

    private void Row(string key, string description)
    {
        _grid.AddChild(new Label { Text = key, StyleClasses = { "LabelKeyText" } });
        _grid.AddChild(new Label
        {
            Text = description,
            StyleClasses = { "LabelSubText" },
            HorizontalExpand = true,
        });
    }
}
