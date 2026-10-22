using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.MappingMode.Panels;

/// <summary>
/// What the current mode works with, what is under the cursor (tile coordinates, grid, tile and entity) and the
/// session's limits line. A dumb view:
/// the screen controller fills it, since a control's own frame update stops while it is hidden.
/// </summary>
public sealed class WFMappingCursorReadout : PanelContainer
{
    private readonly Label _settings;
    private readonly Label _position;
    private readonly Label _grid;
    private readonly Label _tile;
    private readonly Label _entity;
    private readonly Label _limits;

    public WFMappingCursorReadout()
    {
        PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#101014").WithAlpha(0.8f) };
        MouseFilter = MouseFilterMode.Ignore;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(6, 3),
            MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(box);

        _settings = Line(box);
        _settings.FontColorOverride = Color.FromHex("#9ccc65");
        _position = Line(box);
        _grid = Line(box);
        _tile = Line(box);
        _entity = Line(box);
        _limits = Line(box);
        _limits.FontColorOverride = Color.FromHex("#ffb347");
    }

    /// <summary>Shows what is under the cursor; empty strings leave a line blank.</summary>
    public void SetCursor(string position, string grid, string tile, string entity)
    {
        _position.Text = position;
        _grid.Text = grid;
        _grid.Visible = grid.Length > 0;
        _tile.Text = tile;
        _tile.Visible = tile.Length > 0;
        _entity.Text = entity;
        _entity.Visible = entity.Length > 0;
    }

    /// <summary>The current mode's sub-choice, such as the erase target or the shape and its fill.</summary>
    public void SetSettings(string text)
    {
        _settings.Text = text;
        _settings.Visible = text.Length > 0;
    }

    public void SetLimits(string text)
    {
        _limits.Text = text;
        _limits.Visible = text.Length > 0;
    }

    private static Label Line(BoxContainer box)
    {
        var label = new Label { StyleClasses = { "LabelSubText" }, MouseFilter = MouseFilterMode.Ignore };
        box.AddChild(label);
        return label;
    }
}
