using System.Numerics;
using Content.Shared._WG.Settlers;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Settlers;

/// <summary>A hand of resources as five chips in their sectors' colours, each with its icon and count.</summary>
public sealed class SettlersHandRow : BoxContainer
{
    private readonly PanelContainer[] _chips = new PanelContainer[SettlersRules.ResourceCount];
    private readonly Label[] _counts = new Label[SettlersRules.ResourceCount];

    public SettlersHandRow()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 6;
        var font = SettlersArt.Font(16);

        for (var resource = 0; resource < SettlersRules.ResourceCount; resource++)
        {
            var color = SettlersArt.ResourceColor(resource);
            var stack = new BoxContainer { Orientation = LayoutOrientation.Vertical, Align = AlignMode.Center };
            stack.AddChild(new TextureRect
            {
                Texture = SettlersArt.ResourceTexture(resource),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
                MinSize = new Vector2(32f, 32f),
                HorizontalAlignment = HAlignment.Center,
            });

            _counts[resource] = new Label { FontOverride = font, Align = Label.AlignMode.Center };
            stack.AddChild(_counts[resource]);

            _chips[resource] = new PanelContainer
            {
                PanelOverride = SettlersArt.Box(Color.InterpolateBetween(color, Color.Black, 0.72f),
                    Color.InterpolateBetween(color, Color.Black, 0.3f), 1f, 4f, 4f),
                HorizontalExpand = true,
                ToolTip = SettlersArt.ResourceName(resource),
                MouseFilter = MouseFilterMode.Pass,
            };
            _chips[resource].AddChild(stack);
            AddChild(_chips[resource]);
        }
    }

    public void Show(int[] counts)
    {
        for (var resource = 0; resource < _chips.Length; resource++)
        {
            _counts[resource].Text = counts[resource].ToString();
            _chips[resource].Modulate = counts[resource] > 0 ? Color.White : SettlersArt.Dim;
        }
    }
}
