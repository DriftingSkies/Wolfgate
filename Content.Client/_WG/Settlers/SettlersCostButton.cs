using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Settlers;

/// <summary>A button with its name over a strip of the resources it costs; faded while disabled.</summary>
public sealed class SettlersCostButton : ContainerButton
{
    private readonly BoxContainer? _content;
    private readonly Label _label;

    public string? Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public SettlersCostButton(string text, int[] cost)
    {
        AddStyleClass(StyleClassButton);
        HorizontalExpand = true;
        ToolTip = Loc.GetString("wg-settlers-cost", ("cost", SettlersArt.Counts(cost)));

        _label = new Label { Text = text, Align = Label.AlignMode.Center, StyleClasses = { StyleClassButton } };
        _content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(4f, 2f) };
        _content.AddChild(_label);

        var strip = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Align = BoxContainer.AlignMode.Center,
            SeparationOverride = -2,
        };

        for (var resource = 0; resource < cost.Length; resource++)
        {
            for (var i = 0; i < cost[resource]; i++)
            {
                strip.AddChild(new TextureRect
                {
                    Texture = SettlersArt.ResourceTexture(resource),
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    MinSize = new Vector2(14f, 14f),
                });
            }
        }

        _content.AddChild(strip);
        AddChild(_content);
        DrawModeChanged();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        if (_content != null)
            _content.Modulate = Disabled ? SettlersArt.Dim : Color.White;
    }
}
