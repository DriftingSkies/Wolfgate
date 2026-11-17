using Content.Shared._WG.Remade;
using Content.Shared._WG.Remade.Signals;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Remade.Signals;

/// <summary>A Signal's strip on the screen: its name, the stone's stage and its Psi, in the Monolith's black and red.</summary>
public sealed class WGSignalHud : PanelContainer
{
    private readonly Label _title;
    private readonly Label _stage;
    private readonly Label _psi;
    private readonly ProgressBar _bar;

    public WGSignalHud(IResourceCache cache)
    {
        PanelOverride = WGMonolithTheme.Etched(WGMonolithTheme.Vein, 8f);
        MinWidth = 190;

        var text = WGMonolithTheme.Text(cache, 14);
        _title = new Label { Text = Loc.GetString("wg-signal-hud-title"), FontOverride = WGMonolithTheme.Text(cache, 16), FontColorOverride = WGMonolithTheme.Ember };
        _stage = new Label { FontOverride = text, FontColorOverride = WGMonolithTheme.Ash, HorizontalExpand = true, HorizontalAlignment = HAlignment.Right };
        _psi = new Label { FontOverride = text, FontColorOverride = WGMonolithTheme.Bone };
        _bar = new ProgressBar
        {
            MinValue = 0f,
            MaxValue = WGSignalMath.MaxPsi,
            MinHeight = 8,
            BackgroundStyleBoxOverride = WGMonolithTheme.Panel(WGMonolithTheme.Stone, WGMonolithTheme.Seam),
            ForegroundStyleBoxOverride = WGMonolithTheme.Panel(WGMonolithTheme.Ember, WGMonolithTheme.Ember),
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

    public void Update(WGSignalComponent signal)
    {
        _stage.Text = Loc.GetString($"wg-signal-hud-stage-{signal.Stage}");
        _stage.FontColorOverride = WGMonolithTheme.StageColor(signal.Stage);
        _psi.Text = Loc.GetString("wg-signal-hud-psi", ("psi", (int) signal.Psi), ("max", (int) WGSignalMath.MaxPsi));
        _bar.Value = signal.Psi;
    }
}
