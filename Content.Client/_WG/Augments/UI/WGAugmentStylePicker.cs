using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Augments.UI;

/// <summary>
/// The dropdown that picks a cybernetic limb's style. It shows the picked style and, opened, lists every style with
/// the limb drawn in it.
/// </summary>
public sealed class WGAugmentStylePicker : BoxContainer
{
    private const float IconSize = 32;
    private const float MaxListHeight = 360;

    /// <summary>Raised with the index of the style the player picked.</summary>
    public event Action<int>? OnPicked;

    private readonly WGAugmentTheme _theme;
    private readonly Popup _popup;
    private readonly ScrollContainer _scroll;

    public WGAugmentStylePicker(WGAugmentTheme theme, IReadOnlyList<(IReadOnlyList<WGAugmentIconLayer> Icon, string Name)> styles, int selected)
    {
        _theme = theme;
        Orientation = LayoutOrientation.Horizontal;
        HorizontalExpand = true;

        var button = new WGAugmentRowButton(theme.Card, theme.CardHover, theme.CardHover, theme.CardOff) { HorizontalExpand = true };
        IReadOnlyList<WGAugmentIconLayer> icon = Array.Empty<WGAugmentIconLayer>();
        var name = string.Empty;
        if (selected >= 0 && selected < styles.Count)
            (icon, name) = styles[selected];
        var triangle = new TextureRect
        {
            StyleClasses = { OptionButton.StyleClassOptionTriangle },
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        button.AddChild(Entry(icon, name, theme.Text, true, triangle));
        button.OnPressed += _ => Open();
        AddChild(button);

        var list = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 1 };
        _scroll = new ScrollContainer
        {
            HScrollEnabled = false,
            ReturnMeasure = true,
            MaxHeight = MaxListHeight,
            Children = { list },
        };
        _popup = new Popup
        {
            Children =
            {
                new PanelContainer { PanelOverride = theme.Popup, Children = { _scroll } },
            },
        };
        _popup.OnPopupHide += () => _popup.Orphan();

        for (var i = 0; i < styles.Count; i++)
        {
            var index = i;
            var row = new WGAugmentRowButton(theme.Row, theme.RowHover, theme.RowPicked, theme.Row)
            {
                ToggleMode = true,
                Pressed = i == selected,
                HorizontalExpand = true,
            };
            row.AddChild(Entry(styles[i].Icon, styles[i].Name, i == selected ? theme.Accent : theme.Text, false, null));
            row.OnPressed += _ =>
            {
                _popup.Close();
                OnPicked?.Invoke(index);
            };
            list.AddChild(row);
        }
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _popup.Close();
    }

    /// <summary>Opens the list under the picker.</summary>
    public void Open()
    {
        if (Root == null)
            return;

        _scroll.Measure(new Vector2(float.PositiveInfinity, MaxListHeight));
        var margin = _theme.Popup.MinimumSize;
        var size = new Vector2(Math.Max(_scroll.DesiredSize.X + margin.X, Width), _scroll.DesiredSize.Y + margin.Y);
        Root.ModalRoot.AddChild(_popup);
        _popup.Open(UIBox2.FromDimensions(GlobalPosition + new Vector2(0, Height + 1), size));
    }

    /// <summary>A style's icon and name in a row; the picker's own face cuts a name that doesn't fit.</summary>
    private WGAugmentEntry Entry(IReadOnlyList<WGAugmentIconLayer> icon, string name, Color color, bool clip, Control? after)
    {
        var label = _theme.Label(name, color, _theme.Name);
        label.ClipText = clip;
        label.VerticalAlignment = VAlignment.Center;
        return new WGAugmentEntry(_theme.Icon(icon, IconSize), label, after);
    }
}
