using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.MappingMode.Screen;

/// <summary>
/// A docked mapping screen panel with an Undock button. Undocking moves the panel into a
/// <see cref="WFMappingDockWindow"/>; closing that window, or Dock, brings it back.
/// </summary>
public sealed class WFMappingDockSlot : BoxContainer
{
    private readonly string _title;
    private readonly BoxContainer _header;
    private readonly BoxContainer _placeholder;
    private WFMappingDockWindow? _window;

    /// <summary>Holds the panel while it is docked.</summary>
    public readonly BoxContainer Content;

    public bool Undocked => _window != null;

    /// <summary>Raised after the panel moves into its window or back.</summary>
    public event Action? DockChanged;

    public WFMappingDockSlot(string title)
    {
        _title = title;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        _header = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(4, 2) };
        _header.AddChild(new Label
        {
            Text = title,
            StyleClasses = { "LabelSubText" },
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        });
        var undock = new Button
        {
            Text = Loc.GetString("wf-mapping-screen-undock"),
            ToolTip = Loc.GetString("wf-mapping-screen-undock-tooltip"),
        };
        undock.OnPressed += _ => Undock();
        _header.AddChild(undock);
        AddChild(_header);

        Content = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        AddChild(Content);

        _placeholder = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Visible = false,
            Margin = new Thickness(8),
            SeparationOverride = 6,
        };
        _placeholder.AddChild(new Label
        {
            Text = Loc.GetString("wf-mapping-screen-undocked"),
            StyleClasses = { "LabelSubText" },
        });
        var dock = new Button { Text = Loc.GetString("wf-mapping-screen-dock") };
        dock.OnPressed += _ => Dock();
        _placeholder.AddChild(dock);
        AddChild(_placeholder);
    }

    /// <summary>Moves the panel into its own window.</summary>
    public void Undock()
    {
        if (_window != null)
            return;

        var window = new WFMappingDockWindow(_title);
        Move(Content, window.Body);
        Content.Visible = false;
        _header.Visible = false;
        _placeholder.Visible = true;
        window.OnClose += () =>
        {
            if (_window == window)
                Dock();
        };
        // The ways out that skip OnClose, such as the root of a popped-out window's OS window being torn down.
        window.Lost += () =>
        {
            if (_window == window)
                Dock();
        };
        _window = window;
        window.OpenCentered();
        DockChanged?.Invoke();
    }

    /// <summary>Brings the panel back from its window; does nothing while docked.</summary>
    public void Dock()
    {
        if (_window is not { } window)
            return;

        _window = null;
        Move(window.Body, Content);
        Content.Visible = true;
        _header.Visible = true;
        _placeholder.Visible = false;
        if (window.IsOpen)
            window.Close();

        DockChanged?.Invoke();
    }

    private static void Move(Control from, Control to)
    {
        var children = new List<Control>(from.Children);
        foreach (var child in children)
        {
            child.Orphan();
            to.AddChild(child);
        }
    }
}
