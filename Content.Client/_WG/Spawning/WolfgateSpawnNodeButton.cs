using Content.Client._WG.Stylesheets;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Spawning;

public enum WgSpawnNodeKind : byte
{
    All,
    Favourites,
    Recent,
    Group,
    Category,
}

/// <summary>One row of the sidebar: a pseudo-folder, a group folder or a leaf category.</summary>
public readonly record struct WgSpawnNode(WgSpawnNodeKind Kind, WgSpawnGroup Group, WgSpawnCategory Category)
{
    public static readonly WgSpawnNode All = new(WgSpawnNodeKind.All, default, default);
    public static readonly WgSpawnNode Favourites = new(WgSpawnNodeKind.Favourites, default, default);
    public static readonly WgSpawnNode Recent = new(WgSpawnNodeKind.Recent, default, default);

    public static WgSpawnNode Of(WgSpawnGroup group) => new(WgSpawnNodeKind.Group, group, default);
    public static WgSpawnNode Of(WgSpawnCategory category) =>
        new(WgSpawnNodeKind.Category, WgSpawnCategories.GroupFor(category), category);
}

/// <summary>
/// Sidebar row. Folders carry a disclosure arrow and their group colour; leaves are indented under them and
/// every row shows how many entries it holds, or how many match the search.
/// </summary>
public sealed class WolfgateSpawnNodeButton : ContainerButton
{
    private readonly WolfgateSkin _skin;
    private readonly PanelContainer _stripe;
    private readonly Label _label;
    private readonly Label _count;
    private readonly string _text;
    private readonly bool _folder;
    private readonly Color _accent;

    public readonly WgSpawnNode Node;

    public bool Expanded { get; private set; }

    public WolfgateSpawnNodeButton(WgSpawnNode node, string text, Color accent, bool folder, WolfgateSkin skin)
    {
        Node = node;
        _text = text;
        _folder = folder;
        _accent = accent;
        _skin = skin;

        MinHeight = 22;
        ToggleMode = true;
        AddStyleClass(StyleClassButton);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Margin = new Thickness(folder ? 0 : 14, 0, 4, 0),
        };
        AddChild(row);

        _stripe = new PanelContainer
        {
            SetWidth = 3,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat(accent),
        };
        row.AddChild(_stripe);

        _label = new Label
        {
            Text = text,
            ClipText = true,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = folder ? accent : skin.Text,
        };
        row.AddChild(_label);

        _count = new Label
        {
            StyleClasses = { StyleNano.StyleClassLabelSmall },
            FontColorOverride = skin.TextMuted,
            VerticalAlignment = VAlignment.Center,
        };
        row.AddChild(_count);

        SetExpanded(false);
    }

    /// <summary>Shows the entry count; an empty node dims while searching.</summary>
    public void SetCount(int count, bool searching)
    {
        _count.Text = count.ToString();
        // A category with no matches stays visible but reads as empty, so the tree never jumps around mid-search.
        var dim = searching && count == 0;
        _label.FontColorOverride = dim ? _skin.TextDisabled : _folder ? _accent : _skin.Text;
        _count.FontColorOverride = dim ? _skin.TextDisabled : _skin.TextMuted;
    }

    /// <summary>Sets whether a folder node shows as open.</summary>
    public void SetExpanded(bool expanded)
    {
        Expanded = expanded;
        _label.Text = _folder ? $"{(expanded ? "▼" : "▶")} {_text}" : _text;
    }

    /// <summary>Highlights the node as the one being browsed.</summary>
    public void SetActive(bool active)
    {
        Pressed = active;
        _stripe.PanelOverride = new StyleBoxFlat(active ? _accent : _accent.WithAlpha(0.35f));
    }
}
