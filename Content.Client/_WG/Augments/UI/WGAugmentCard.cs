using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Augments.UI;

/// <summary>A button that is a framed row of anything, lit while the mouse is on it or it is picked.</summary>
public sealed class WGAugmentRowButton : ContainerButton
{
    private readonly StyleBox _normal;
    private readonly StyleBox _hover;
    private readonly StyleBox _picked;
    private readonly StyleBox _off;

    public WGAugmentRowButton(StyleBox normal, StyleBox hover, StyleBox picked, StyleBox off)
    {
        _normal = normal;
        _hover = hover;
        _picked = picked;
        _off = off;
        DrawModeChanged();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        // The base constructor calls this before the boxes are set.
        if (_normal == null)
            return;

        StyleBoxOverride = DrawMode switch
        {
            DrawModeEnum.Pressed => _picked,
            DrawModeEnum.Hover => _hover,
            DrawModeEnum.Disabled => _off,
            _ => _normal,
        };
    }
}

/// <summary>
/// An icon, text and an amount in a row. The icon and the amount take what they need and the text gets the rest, so
/// text that wraps never pushes the amount off the edge.
/// </summary>
public sealed class WGAugmentEntry : Container
{
    private const float Gap = 8;

    private readonly Control _icon;
    private readonly Control _text;
    private readonly Control? _amount;

    public WGAugmentEntry(Control icon, Control text, Control? amount = null)
    {
        MouseFilter = MouseFilterMode.Ignore;
        _icon = icon;
        _text = text;
        _amount = amount;
        AddChild(icon);
        AddChild(text);
        if (amount != null)
            AddChild(amount);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _icon.Measure(availableSize);
        var taken = _icon.DesiredSize.X + Gap;
        var height = _icon.DesiredSize.Y;
        if (_amount != null)
        {
            _amount.Measure(availableSize);
            taken += _amount.DesiredSize.X + Gap;
            height = Math.Max(height, _amount.DesiredSize.Y);
        }

        _text.Measure(new Vector2(Math.Max(availableSize.X - taken, 0), availableSize.Y));
        return new Vector2(taken + _text.DesiredSize.X, Math.Max(height, _text.DesiredSize.Y));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var left = _icon.DesiredSize.X;
        _icon.Arrange(UIBox2.FromDimensions(0, 0, left, finalSize.Y));
        left += Gap;

        var right = finalSize.X;
        if (_amount != null)
        {
            right = Math.Max(right - _amount.DesiredSize.X, left);
            _amount.Arrange(new UIBox2(right, 0, Math.Max(finalSize.X, right), finalSize.Y));
            right = Math.Max(right - Gap, left);
        }

        _text.Arrange(new UIBox2(left, 0, right, finalSize.Y));
        return finalSize;
    }
}

/// <summary>What a card shows of one choice.</summary>
public readonly record struct WGAugmentCardInfo(
    IReadOnlyList<WGAugmentIconLayer> Icon,
    string Name,
    string? Description,
    string? Warning,
    string Price,
    Color PriceColor);

/// <summary>Builds the creator's choice cards: an icon, the name over what it does, and the price.</summary>
public static class WGAugmentCard
{
    public const float IconSize = 40;

    public static WGAugmentRowButton Create(WGAugmentTheme theme, WGAugmentCardInfo info, bool picked, bool disabled, Action onPressed)
    {
        var card = new WGAugmentRowButton(theme.Card, theme.CardHover, theme.CardPicked, theme.CardOff)
        {
            ToggleMode = true,
            Pressed = picked,
            Disabled = disabled,
            HorizontalExpand = true,
        };
        card.OnPressed += _ => onPressed();

        var text = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalAlignment = Control.VAlignment.Center,
            MouseFilter = Control.MouseFilterMode.Ignore,
        };
        text.AddChild(theme.WrappedName(info.Name, disabled ? theme.Faint : picked ? theme.Accent : theme.Text, true));
        if (!string.IsNullOrEmpty(info.Warning))
            text.AddChild(theme.Wrapped(info.Warning, disabled ? theme.Bad : theme.Price));
        if (!string.IsNullOrEmpty(info.Description))
            text.AddChild(theme.Wrapped(info.Description, disabled ? theme.Faint : theme.Muted));

        Label? price = null;
        if (!string.IsNullOrEmpty(info.Price))
        {
            price = theme.Label(info.Price, disabled ? info.PriceColor.WithAlpha(0.45f) : info.PriceColor, theme.Amount);
            price.VerticalAlignment = Control.VAlignment.Center;
        }

        card.AddChild(new WGAugmentEntry(theme.Icon(info.Icon, IconSize, disabled), text, price));
        return card;
    }
}
