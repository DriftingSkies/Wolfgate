using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._WF.Modsuits;

/// <summary>
/// A button that shows one of the MODsuit Font Awesome icons with optional text.
/// </summary>
public sealed class ModSuitIconButton : Button
{
    public readonly TextureRect Icon;
    private readonly BoxContainer _content;

    public ModSuitIconButton()
    {
        RemoveChild(Label);

        _content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            SeparationOverride = 4,
        };

        Icon = new TextureRect
        {
            MinSize = new Vector2(16, 16),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            VerticalAlignment = VAlignment.Center,
            Visible = false,
        };

        _content.AddChild(Icon);
        _content.AddChild(Label);
        AddChild(_content);
    }

    public Texture? IconTexture
    {
        get => Icon.Texture;
        set
        {
            Icon.Texture = value;
            Icon.Visible = value != null;
        }
    }

    public Color IconTint
    {
        set => Icon.ModulateSelfOverride = value;
    }

    /// <summary>Centres the icon and hides the text, for compact table actions.</summary>
    public bool IconOnly
    {
        set
        {
            Label.Visible = !value;
            Icon.HorizontalExpand = value;
            Icon.HorizontalAlignment = value ? HAlignment.Center : HAlignment.Left;
            _content.SeparationOverride = value ? 0 : 4;
        }
    }
}
