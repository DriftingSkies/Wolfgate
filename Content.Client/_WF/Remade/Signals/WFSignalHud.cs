using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Signals;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Remade.Signals;

/// <summary>A Signal's strip on the screen: its name, the stone's stage and its Psi, in the Monolith's black and red.</summary>
public sealed class WFSignalHud : PanelContainer
{
    private readonly Label _title;
    private readonly Label _stage;
    private readonly Label _psi;
    private readonly ProgressBar _bar;

    public WFSignalHud(IResourceCache cache)
    {
        PanelOverride = WFMonolithTheme.Etched(WFMonolithTheme.Vein, 8f);
        MinWidth = 190;

        var text = WFMonolithTheme.Text(cache, 14);
        _title = new Label { Text = Loc.GetString("wf-signal-hud-title"), FontOverride = WFMonolithTheme.Text(cache, 16), FontColorOverride = WFMonolithTheme.Ember };
        _stage = new Label { FontOverride = text, FontColorOverride = WFMonolithTheme.Ash, HorizontalExpand = true, HorizontalAlignment = HAlignment.Right };
        _psi = new Label { FontOverride = text, FontColorOverride = WFMonolithTheme.Bone };
        _bar = new ProgressBar
        {
            MinValue = 0f,
            MaxValue = WFSignalMath.MaxPsi,
            MinHeight = 8,
            BackgroundStyleBoxOverride = WFMonolithTheme.Panel(WFMonolithTheme.Stone, WFMonolithTheme.Seam),
            ForegroundStyleBoxOverride = WFMonolithTheme.Panel(WFMonolithTheme.Ember, WFMonolithTheme.Ember),
        };

        var head = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        head.AddChild(_title);
        head.AddChild(_stage);

        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };
        column.AddChild(head);
        column.AddChild(_bar);
        column.AddChild(_psi);
        AddChild(column);
    }

    public void Update(WFSignalComponent signal)
    {
        _stage.Text = Loc.GetString($"wf-signal-hud-stage-{signal.Stage}");
        _stage.FontColorOverride = WFMonolithTheme.StageColor(signal.Stage);
        _psi.Text = Loc.GetString("wf-signal-hud-psi", ("psi", (int) signal.Psi), ("max", (int) WFSignalMath.MaxPsi));
        _bar.Value = signal.Psi;
    }
}
