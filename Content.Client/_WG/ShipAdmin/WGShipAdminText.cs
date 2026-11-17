using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.ShipAdmin;

/// <summary>Plain text that wraps to the width it is given, where a label would run off the panel.</summary>
public sealed class WGShipAdminText : RichTextLabel
{
    /// <summary>The colour of notes and other secondary text.</summary>
    public static readonly Color Subdued = Color.FromHex("#A0A0A0");

    private string _text = string.Empty;
    private Color? _colour;

    public WGShipAdminText()
    {
        HorizontalExpand = true;
        VerticalAlignment = VAlignment.Top;
    }

    public WGShipAdminText(string text, Color? colour = null) : this()
    {
        _colour = colour;
        Text = text;
    }

    /// <summary>The text shown. It is never read as markup, so names with brackets show as they are.</summary>
    public new string Text
    {
        get => _text;
        set
        {
            _text = value;
            SetMessage(value, _colour);
        }
    }

    /// <summary>The text's colour; null for the default.</summary>
    public Color? Colour
    {
        get => _colour;
        set
        {
            _colour = value;
            SetMessage(_text, _colour);
        }
    }
}
