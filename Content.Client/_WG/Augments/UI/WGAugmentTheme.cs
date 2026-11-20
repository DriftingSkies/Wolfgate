using System.Numerics;
using Content.Client._WG.Stylesheets;
using Content.Client.Resources;
using Content.Shared._WG.CCVar;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Client._WG.Augments.UI;

/// <summary>The Augments tab's colors, frames and fonts, taken from the player's Wolfgate skin.</summary>
public sealed class WGAugmentTheme
{
    public readonly Color Accent;
    public readonly Color Price;
    public readonly Color Good;
    public readonly Color Bad;
    public readonly Color Text;
    public readonly Color Muted;
    public readonly Color Faint;
    public readonly Color Line;
    public readonly Color Plate;

    /// <summary>The square an icon sits in, light enough for a dark sprite to show on.</summary>
    public readonly StyleBox IconWell;

    public readonly StyleBox Card;
    public readonly StyleBox CardHover;
    public readonly StyleBox CardPicked;
    public readonly StyleBox CardOff;

    /// <summary>A row with no frame until the mouse is on it.</summary>
    public readonly StyleBox Row;
    public readonly StyleBox RowHover;
    public readonly StyleBox RowPicked;

    public readonly StyleBox Popup;

    /// <summary>The font of names.</summary>
    public readonly Font Name;

    /// <summary>The font of section headings.</summary>
    public readonly Font Heading;

    /// <summary>The font of amounts, and the larger one of the bill's total.</summary>
    public readonly Font Amount;
    public readonly Font Total;

    public WGAugmentTheme()
    {
        var skin = WolfgateSkins.Get(IoCManager.Resolve<IConfigurationManager>().GetCVar(WolfgateCVars.UiStyle));
        var cache = IoCManager.Resolve<IResourceCache>();

        Accent = skin.Accent;
        Price = skin.Caution;
        Good = skin.Good;
        Bad = skin.Danger;
        Text = skin.Text;
        Muted = skin.TextMuted;
        Faint = skin.TextMuted.WithAlpha(0.6f);
        Line = skin.Edge;
        Plate = skin.Ink.WithAlpha(0.9f);

        var picked = Color.InterpolateBetween(skin.GlassLight, skin.Selection, 0.6f);
        IconWell = Box(Color.InterpolateBetween(skin.GlassLight, skin.EdgeLight, 0.5f), skin.Edge, new Thickness(1), 3, 3);
        Card = Box(skin.GlassLight, skin.Edge, new Thickness(1), 8, 6);
        CardHover = Box(skin.ButtonHovered, skin.EdgeLight, new Thickness(1), 8, 6);
        CardPicked = Box(picked, skin.Accent, new Thickness(3, 1, 1, 1), 8, 6);
        CardOff = Box(skin.Glass, skin.EdgeSoft, new Thickness(1), 8, 6);
        Row = Box(Color.Transparent, Color.Transparent, new Thickness(1), 4, 3);
        RowHover = Box(skin.ButtonHovered, skin.EdgeLight, new Thickness(1), 4, 3);
        RowPicked = Box(picked, skin.Accent, new Thickness(1), 4, 3);
        Popup = Box(skin.Glass, skin.EdgeLight, new Thickness(1), 3, 3);

        Name = cache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 12);
        Heading = cache.GetFont(skin.DisplayFonts, 11);
        Amount = cache.GetFont(skin.MonoFonts, 12);
        Total = cache.GetFont(skin.MonoFonts, 15);
    }

    /// <summary>A flat box with its border counted into the padding.</summary>
    private static StyleBoxFlat Box(Color background, Color border, Thickness thickness, float padX, float padY)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = thickness,
            ContentMarginLeftOverride = thickness.Left + padX,
            ContentMarginRightOverride = thickness.Right + padX,
            ContentMarginTopOverride = thickness.Top + padY,
            ContentMarginBottomOverride = thickness.Bottom + padY,
        };
    }

    /// <summary>A label that lets clicks through to the button under it.</summary>
    public Label Label(string text, Color color, Font? font = null, bool small = false)
    {
        var label = new Label { Text = text, FontColorOverride = color, FontOverride = font, MouseFilter = Control.MouseFilterMode.Ignore };
        if (small)
            label.StyleClasses.Add("LabelSubText");
        return label;
    }

    /// <summary>Small text that wraps and lets clicks through.</summary>
    public RichTextLabel Wrapped(string text, Color color)
    {
        var label = new RichTextLabel { StyleClasses = { "LabelSubText" } };
        label.SetMessage(text, color);
        label.MouseFilter = Control.MouseFilterMode.Ignore;
        return label;
    }

    /// <summary>A name that wraps instead of being cut short.</summary>
    public RichTextLabel WrappedName(string text, Color color, bool bold)
    {
        var label = new RichTextLabel();
        var message = new FormattedMessage();
        if (bold)
            message.PushTag(new MarkupNode("bold", null, null));
        message.AddText(text);
        if (bold)
            message.Pop();
        label.SetMessage(message, color);
        label.MouseFilter = Control.MouseFilterMode.Ignore;
        return label;
    }

    /// <summary>An icon in its well.</summary>
    public Control Icon(IReadOnlyList<WGAugmentIconLayer> layers, float size, bool dimmed = false)
    {
        return new PanelContainer
        {
            PanelOverride = IconWell,
            MouseFilter = Control.MouseFilterMode.Ignore,
            VerticalAlignment = Control.VAlignment.Center,
            Children = { new WGAugmentIcon(size, layers) { Dimmed = dimmed } },
        };
    }

    /// <summary>A section's name with a rule running to the edge.</summary>
    public Control Section(string text, Control? after = null)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(0, 8, 0, 2),
        };
        row.AddChild(Label(text, Muted, Heading));
        if (after != null)
            row.AddChild(after);
        row.AddChild(new WGAugmentRule(Line) { VerticalAlignment = Control.VAlignment.Center });
        return row;
    }

    /// <summary>A thin line across a panel.</summary>
    public Control Rule()
    {
        return new WGAugmentRule(Line);
    }
}

/// <summary>A horizontal line one screen pixel thick, whatever the UI scale.</summary>
public sealed class WGAugmentRule : Control
{
    private readonly Color _color;

    public WGAugmentRule(Color color)
    {
        _color = color;
        MinSize = new Vector2(0, 3);
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var y = MathF.Floor(PixelSize.Y / 2f);
        handle.DrawRect(new UIBox2(0, y, PixelSize.X, y + 1), _color);
    }
}
