using Content.Client._WG.MappingMode.Modes;
using Content.Shared._WG.MappingMode;
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._WG.MappingMode.Panels;

/// <summary>The mapping screen's keys: the mode keys with their bindings, each mode's mouse actions and the tools' keys.</summary>
public sealed partial class WGMappingKeysPanel : BoxContainer
{
    [Dependency] private IInputManager _input = default!;

    private static readonly BoundKeyFunction[] ToolKeys =
    {
        ContentKeyFunctions.WGMappingEnableSelect,
        ContentKeyFunctions.WGMappingRotate,
        ContentKeyFunctions.WGMappingMirror,
        ContentKeyFunctions.WGMappingMirrorVertical,
        ContentKeyFunctions.WGMappingDelete,
        ContentKeyFunctions.WGMappingCopy,
        ContentKeyFunctions.WGMappingCut,
        ContentKeyFunctions.WGMappingPaste,
        ContentKeyFunctions.WGMappingUndo,
        ContentKeyFunctions.WGMappingRedo,
        ContentKeyFunctions.WGMappingLevelUp,
        ContentKeyFunctions.WGMappingLevelDown,
        WGMappingKeyFunctions.WGMappingModeCancel,
        WGMappingKeyFunctions.WGMappingLinkCycle,
        WGMappingKeyFunctions.WGMappingQuicksave,
    };

    private readonly GridContainer _grid;

    public WGMappingKeysPanel()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        var guide = new Button
        {
            Text = Loc.GetString("wg-mapping-guide-button"),
            ToolTip = Loc.GetString("wg-mapping-guide-button-tooltip"),
            HorizontalAlignment = HAlignment.Left,
            Margin = new Thickness(6, 6, 6, 0),
        };
        guide.OnPressed += _ => WGMappingGuideLauncher.Open();
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

        Heading("wg-mapping-screen-keys-modes");
        for (var i = 0; i < WGMappingKeyFunctions.Modes.Length; i++)
        {
            var mode = ((WGMappingBuildMode) (i + 1)).ToString().ToLowerInvariant();
            Row(Bound(WGMappingKeyFunctions.Modes[i]), Loc.GetString($"wg-mapping-screen-mode-{mode}"));
        }

        Heading("wg-mapping-screen-keys-mouse");
        foreach (var mode in Enum.GetValues<WGMappingBuildMode>())
        {
            var name = mode.ToString().ToLowerInvariant();
            Row(Loc.GetString($"wg-mapping-screen-mode-{name}"), Loc.GetString($"wg-mapping-screen-mouse-{name}"));
        }

        Row(Loc.GetString("wg-mapping-screen-keys-ctrl-wheel"), Loc.GetString("wg-mapping-screen-keys-ctrl-wheel-desc"));

        Heading("wg-mapping-screen-keys-tools");
        foreach (var function in ToolKeys)
        {
            Row(Bound(function), Loc.GetString($"wg-mapping-screen-key-{function.FunctionName}"));
        }
    }

    private string Bound(BoundKeyFunction function)
    {
        var text = _input.GetKeyFunctionButtonString(function);
        return string.IsNullOrEmpty(text) ? Loc.GetString("wg-mapping-screen-key-unbound") : text;
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
