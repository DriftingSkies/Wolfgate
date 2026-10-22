using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Spawning;

/// <summary>
/// One entry in the spawn grid: group stripe, sprite, name, editor suffix and a favourite star. Tiles are pooled,
/// so a tile is handed a new entry rather than being thrown away when the grid scrolls.
/// </summary>
public sealed class WolfgateSpawnTile : ContainerButton
{
    private const string FavouriteGlyph = "★";

    private static readonly Color NoteColor = Color.FromHex("#e0a030");

    private readonly PanelContainer _stripe;
    private readonly TextureRect _icon;
    private readonly Label _name;
    private readonly Label _suffix;
    private readonly Label _note;
    private readonly Button _favourite;

    private WolfgateSkin _skin;

    public WolfgateSpawnEntry? Entry { get; private set; }

    /// <summary>Raised by the star, not by selecting the tile.</summary>
    public event Action<WolfgateSpawnEntry>? OnFavouriteToggled;

    public WolfgateSpawnTile(WolfgateSkin skin)
    {
        _skin = skin;
        ToggleMode = true;
        AddStyleClass(StyleClassButton);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        AddChild(row);

        _stripe = new PanelContainer
        {
            SetWidth = 3,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat(skin.Accent),
        };
        row.AddChild(_stripe);

        _icon = new TextureRect
        {
            SetSize = new Vector2(32, 32),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            VerticalAlignment = VAlignment.Center,
        };
        row.AddChild(_icon);

        var text = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        row.AddChild(text);

        _name = new Label { ClipText = true };
        text.AddChild(_name);

        _suffix = new Label
        {
            ClipText = true,
            Visible = false,
            StyleClasses = { StyleNano.StyleClassLabelSmall },
            FontColorOverride = skin.TextMuted,
        };
        text.AddChild(_suffix);

        _note = new Label
        {
            Text = Loc.GetString("wf-mapping-screen-unsaved-mark"),
            Visible = false,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = NoteColor,
        };
        row.AddChild(_note);

        _favourite = new Button
        {
            Text = FavouriteGlyph,
            MinWidth = 24,
            VerticalAlignment = VAlignment.Center,
            ToolTip = Loc.GetString("wf-spawn-favourite-tooltip"),
        };
        _favourite.OnPressed += _ =>
        {
            if (Entry != null)
                OnFavouriteToggled?.Invoke(Entry);
        };
        row.AddChild(_favourite);
    }

    /// <summary>
    /// Points the tile at a different entry, or blanks it when the grid has fewer entries than tiles. A
    /// <paramref name="note"/> marks the tile with a warning and is added to its tooltip.
    /// </summary>
    public void SetEntry(WolfgateSpawnEntry? entry, Texture? icon, Color accent, bool favourite, bool selected,
        string? note = null)
    {
        Entry = entry;
        Visible = entry != null;

        if (entry == null)
        {
            Pressed = false;
            _icon.Texture = null;
            return;
        }

        _stripe.PanelOverride = new StyleBoxFlat(accent);
        _icon.Texture = icon;
        _name.Text = entry.Name;
        _name.FontColorOverride = _skin.Text;

        _suffix.Visible = entry.Suffix.Length > 0;
        _suffix.Text = entry.Suffix;

        _favourite.Modulate = favourite ? Color.White : _skin.TextDisabled;

        var description = string.IsNullOrEmpty(entry.Prototype.Description)
            ? Loc.GetString("wf-spawn-no-description")
            : entry.Prototype.Description;
        ToolTip = $"{entry.Name}\n{entry.Id}\n{description}";

        _note.Visible = note != null;
        if (note != null)
            ToolTip += $"\n\n{note}";

        Pressed = selected;
    }

    /// <summary>Applies a new UI skin's colours.</summary>
    public void SetSkin(WolfgateSkin skin)
    {
        _skin = skin;
        _suffix.FontColorOverride = skin.TextMuted;
    }
}
