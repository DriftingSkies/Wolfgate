using System.Numerics;
using Content.Client._WG.Cockpit;
using Content.Client._WG.CombatConsole;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleDockControl
{
    /// <summary>Smallest click radius around a port, in UI units, so a port stays clickable when the plot is zoomed out.</summary>
    private const float WgPortReachMinimum = 14f;

    private BoxContainer? _wgCockpitDockActions;
    private readonly Dictionary<DockingPortState, int> _wgCockpitDockNumbers = new();
    private readonly List<UIBox2> _wgCockpitDockLabels = new();
    private readonly Dictionary<NetEntity, UIBox2> _wgCockpitDockCallouts = new();
    private readonly Dictionary<NetEntity, UIBox2> _wgCockpitDockOffers = new();
    private readonly List<WGDockTarget> _wgPortTargets = new();
    private readonly List<DockingPortState> _wgPortStates = new();
    private NetEntity? _wgPressedPort;
    private NetEntity? _wgPressedOffer;
    private NetEntity? _wgHoveredPort;

    /// <summary>Translates the dock drawing while retaining its real docking coordinates.</summary>
    private Vector2 WgCockpitDockPan => WgCockpitControls
        ? new Vector2(Offset.X, -Offset.Y) * MinimapScale
        : Vector2.Zero;

    /// <summary>Places existing port actions below the plot, retaining their docking checks and events.</summary>
    public Control WgCockpitDockActions(WGCockpitLease lease)
    {
        var actions = new BoxContainer
        {
            Name = "CockpitDockPortActions",
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
        _wgCockpitDockActions = actions;
        BuildDocks(GridEntity);
        lease.Remember(() =>
        {
            _wgCockpitDockActions = null;
            Offset = Vector2.Zero;
            // The window can be torn down after its grid is gone, as when the BUI closes with the map.
            if (EntManager.EntityExists(GridEntity))
                BuildDocks(GridEntity);
        });
        return actions;
    }

    private void WgBeginCockpitDockDraw()
    {
        _wgCockpitDockLabels.Clear();
        _wgCockpitDockCallouts.Clear();
        _wgCockpitDockOffers.Clear();
    }

    /// <summary>Whether the viewed port can dock with this one right now: its list action is a live Dock button.</summary>
    private bool WgDockOffered(DockingPortState dock) =>
        ViewedDock != null && !dock.Connected && dock.GridDockedWith == null &&
        _dockButtons.TryGetValue(dock, out var action) && action is { Visible: true, Disabled: false };

    /// <summary>The port whose DOCK key on the plot is under a point, if any.</summary>
    private NetEntity? WgOfferAt(Vector2 pixel)
    {
        foreach (var (entity, key) in _wgCockpitDockOffers)
        {
            if (key.Contains(pixel))
                return entity;
        }

        return null;
    }

    /// <summary>Sends the request the port's list action sends, if it is still on offer.</summary>
    private void WgRequestDock(NetEntity target)
    {
        if (ViewedDock is not { } viewed)
            return;

        foreach (var dock in _dockButtons.Keys)
        {
            if (dock.Entity != target || !WgDockOffered(dock))
                continue;

            UserInterfaceManager.ClickSound();
            _nextDockChange = _timing.CurTime + DockChangeCooldown;
            DockRequest?.Invoke(viewed, target);
            return;
        }
    }

    private bool WgBuildCockpitDockRow(DockingPortState dock, Button action, PanelContainer original)
    {
        if (_wgCockpitDockActions == null)
            return false;

        action.Parent!.RemoveChild(action);
        original.Orphan();
        var number = _wgCockpitDockNumbers.Count + 1;
        _wgCockpitDockNumbers.Add(dock, number);
        var name = dock.LabelName ?? dock.Name;
        var caption = new Label
        {
            Text = Loc.GetString("wg-cockpit-dock-port", ("number", number), ("name", name)),
            ClipText = true,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        action.Name = "CockpitDockAction";
        action.SetWidth = action.MinWidth = 110;
        action.HorizontalExpand = false;
        action.HorizontalAlignment = HAlignment.Stretch;
        action.AddStyleClass("WgCompact");
        var row = WGInstrumentTheme.Row(caption, action);
        // Labels and containers ignore the mouse by default, so the row takes it for the tooltip and hover.
        row.MouseFilter = MouseFilterMode.Pass;
        row.ToolTip = name;
        row.Visible = false;
        Action<GUIMouseHoverEventArgs> highlight = _ => HighlightedDock = dock.Entity;
        Action<GUIMouseHoverEventArgs> clear = _ => HighlightedDock = null;
        row.OnMouseEntered += highlight;
        row.OnMouseExited += clear;
        action.OnMouseEntered += highlight;
        action.OnMouseExited += clear;
        _wgCockpitDockActions.AddChild(row);
        _dockContainers[dock] = row;
        WGInstrumentTheme.Apply(row);
        return true;
    }

    private bool WgDrawCockpitDockMarker(DrawingHandleScreen handle, DockingPortState dock,
        bool visible, Matrix3x2 gridToView)
    {
        if (_wgCockpitDockActions == null)
            return false;

        _dockContainers[dock].Visible = visible;
        if (!visible)
            return true;

        var position = Vector2.Transform(dock.Coordinates.Position, gridToView);
        // A port the ship can dock with right now gets a key on the plot instead of a bare number.
        var offered = WgDockOffered(dock);
        var number = _wgCockpitDockNumbers[dock];
        var text = offered ? Loc.GetString("wg-cockpit-dock-offer", ("number", number)) : number.ToString();
        var scale = UIScale * (offered ? 1f : 0.85f);
        var dimensions = handle.GetDimensions(Font, text, scale);
        var markerSize = offered
            ? Vector2.Max(new Vector2(64, 26) * UIScale, dimensions + new Vector2(16, 10) * UIScale)
            : Vector2.Max(new Vector2(20, 18) * UIScale, dimensions + new Vector2(6, 4) * UIScale);
        var marker = WGDockMarkerLayout.Place(position, markerSize, PixelSize, _wgCockpitDockLabels);
        if (marker is not { } bounds)
            return true;
        _wgCockpitDockLabels.Add(bounds);
        _wgCockpitDockCallouts[dock.Entity] = bounds;
        if (offered)
        {
            _wgCockpitDockOffers[dock.Entity] = bounds;
            var held = _wgPressedOffer == dock.Entity;
            var face = held ? WGInstrumentTheme.Cream : WGInstrumentTheme.Green;
            if (!held && HighlightedDock == dock.Entity)
                face = Color.InterpolateBetween(face, WGInstrumentTheme.Cream, 0.35f);
            handle.DrawLine(position, bounds.Center, face.WithAlpha(0.8f));
            handle.DrawRect(bounds, face);
            handle.DrawRect(bounds, WGInstrumentTheme.Cream, false);
            handle.DrawString(Font, bounds.Center - dimensions / 2, text, scale, WGInstrumentTheme.Ink);
            return true;
        }
        var color = HighlightedDock == dock.Entity ? WGInstrumentTheme.Cream : WGInstrumentTheme.Accent;
        handle.DrawLine(position, bounds.Center, color.WithAlpha(0.65f));
        handle.DrawRect(bounds, WGInstrumentTheme.Ink.WithAlpha(0.95f));
        handle.DrawRect(bounds, color, false);
        handle.DrawString(Font, bounds.Center - dimensions / 2, text, scale, color);
        return true;
    }

    /// <summary>Where one of this ship's ports is plotted, in pixels from the plot's corner; the plot stays north-up around the viewed port.</summary>
    private Vector2? WgPortPlotPosition(DockingPortState port)
    {
        if (_viewedState == null)
            return null;

        var offset = port.Coordinates.Position - _viewedState.Coordinates.Position;
        return MidPointVector + new Vector2(offset.X, -offset.Y) * MinimapScale - WgCockpitDockPan;
    }

    /// <summary>The unselected port of this ship whose callout or body is under a point, if any; another ship's callout over it blocks the click.</summary>
    private DockingPortState? WgPortAt(Vector2 pixel)
    {
        if (DockState == null || GridEntity is not { } grid ||
            !DockState.Docks.TryGetValue(EntManager.GetNetEntity(grid), out var ports))
            return null;

        foreach (var (entity, callout) in _wgCockpitDockCallouts)
        {
            if (!callout.Contains(pixel))
                continue;

            var own = false;
            foreach (var port in ports)
            {
                if (port.Entity != entity)
                    continue;

                own = true;
                break;
            }

            if (!own)
                return null;
        }

        _wgPortTargets.Clear();
        _wgPortStates.Clear();
        var reach = MathF.Max(0.6f * MinimapScale, WgPortReachMinimum * UIScale);
        foreach (var port in ports)
        {
            if (port.Entity == ViewedDock || WgPortPlotPosition(port) is not { } center)
                continue;

            UIBox2? callout = _wgCockpitDockCallouts.TryGetValue(port.Entity, out var box) ? box : null;
            _wgPortTargets.Add(new WGDockTarget(center, reach, callout));
            _wgPortStates.Add(port);
        }

        var hit = WGDockMarkerLayout.Pick(pixel, _wgPortTargets);
        return hit < 0 ? null : _wgPortStates[hit];
    }

    /// <summary>Takes a left press on one of this ship's ports; its release selects it.</summary>
    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Handled || args.Function != EngineKeyFunctions.UIClick)
            return;

        _wgPressedPort = null;
        _wgPressedOffer = WgOfferAt(args.RelativePixelPosition);
        if (_wgPressedOffer != null)
        {
            args.Handle();
            return;
        }

        if (WgPortAt(args.RelativePixelPosition) is not { } port)
            return;

        _wgPressedPort = port.Entity;
        args.Handle();
    }

    /// <summary>Views the port the press began on when the release is still over it, as the port list does.</summary>
    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Handled || args.Function != EngineKeyFunctions.UIClick)
            return;

        var offer = _wgPressedOffer;
        _wgPressedOffer = null;
        if (offer != null)
        {
            // Released over the key it was pressed on, like any button.
            if (WgOfferAt(args.RelativePixelPosition) == offer)
                WgRequestDock(offer.Value);
            args.Handle();
            return;
        }

        var pressed = _wgPressedPort;
        _wgPressedPort = null;
        if (pressed == null || WgPortAt(args.RelativePixelPosition) is not { } port || port.Entity != pressed)
            return;

        UserInterfaceManager.ClickSound();
        SetViewedDock(port);
        args.Handle();
    }

    /// <summary>A press has no release once the plot is hidden, so it must not wait for one.</summary>
    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (!newVisible)
            _wgPressedPort = _wgPressedOffer = null;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _wgPressedPort = _wgPressedOffer = null;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        WgHoverPort(WgOfferAt(args.RelativePixelPosition) ?? WgPortAt(args.RelativePixelPosition)?.Entity);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        WgHoverPort(null);
    }

    /// <summary>Highlights the port under the pointer, as hovering its list entry does, and shows it can be clicked.</summary>
    private void WgHoverPort(NetEntity? hovered)
    {
        if (hovered == _wgHoveredPort)
            return;

        if (_wgHoveredPort != null && HighlightedDock == _wgHoveredPort)
            HighlightedDock = null;
        if (hovered != null)
            HighlightedDock = hovered;
        _wgHoveredPort = hovered;
        DefaultCursorShape = hovered != null ? CursorShape.Hand : CursorShape.Arrow;
    }
}
