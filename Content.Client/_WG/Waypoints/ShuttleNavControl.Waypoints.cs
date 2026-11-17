using System.Numerics;
using Content.Client._WG.CombatConsole;
using Content.Client._WG.Lattice;
using Content.Client._WG.Waypoints;
using Content.Shared._Mono.Shuttles;
using Content.Shared._WG.Waypoints;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private const float WgWaypointMarkerSize = 6f;
    private const float WgOverlayEdge = 8f;
    /// <summary>Clears the terrain toggle above the waypoint key.</summary>
    private const float WgWaypointKeyTop = 44f;
    private const float WgWaypointPanelTop = 80f;
    /// <summary>Clears the cockpit's contact toggles in the same corner.</summary>
    private const float WgCockpitReadoutClearance = 44f;

    private static readonly Color WgWaypointColor = Color.FromHex("#6EE7FF");
    private static readonly Color WgReadoutColor = Color.FromHex("#C8D2C8");
    private static readonly Color WgReadoutShadow = Color.Black.WithAlpha(0.8f);
    private static readonly IReadOnlyList<WGWaypoint> WgNoWaypoints = new List<WGWaypoint>();

    private readonly Vector2[] _wgWaypointVertices = new Vector2[5];
    private Enum? _wgWaypointKey;
    private bool _wgWaypointCanFly;
    private bool _wgWaypointPicking;
    private bool _wgSwallowRelease;
    private Button? _wgWaypointButton;
    private WGWaypointPanel? _wgWaypointPanel;

    /// <summary>Gives this radar the waypoint panel, talking through the given console window; only a helm can fly to them.</summary>
    public void WgEnableWaypoints(Enum uiKey, bool canFly)
    {
        _wgWaypointKey = uiKey;
        _wgWaypointCanFly = canFly;
    }

    /// <summary>The world position the cursor is over, or null with no cursor on the radar.</summary>
    public Vector2? WgCursorWorldPosition(Vector2 relativePosition)
    {
        var coordinates = GetMouseEntityCoordinates(relativePosition);
        return coordinates.IsValid(EntManager) ? _transform.ToMapCoordinates(coordinates).Position : null;
    }

    /// <summary>The world position of the console this radar belongs to, else of the point it is centred on.</summary>
    public Vector2? WgScannerWorldPosition()
    {
        if (EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console))
            return _transform.GetWorldPosition(console);
        return _coordinates is { } centre && centre.IsValid(EntManager)
            ? _transform.ToMapCoordinates(centre).Position
            : null;
    }

    private void AddWgWaypointControls(LayoutContainer parent)
    {
        if (_wgWaypointKey == null)
            return;

        var button = _wgWaypointButton = new Button
        {
            Name = "WgWaypointKey",
            Text = Loc.GetString("wg-waypoint-key"),
            ToolTip = Loc.GetString("wg-waypoint-key-tooltip"),
            ToggleMode = true,
        };
        var panel = _wgWaypointPanel = new WGWaypointPanel { Visible = false };
        button.OnToggled += args =>
        {
            panel.Visible = args.Pressed;
            if (!args.Pressed)
                panel.CancelPick();
        };
        panel.PickToggled += picking => _wgWaypointPicking = picking;
        panel.AddRequested += (name, position) => WgSendWaypointMessage(new WGWaypointAddMessage(name, position));
        panel.RemoveRequested += id => WgSendWaypointMessage(new WGWaypointRemoveMessage(id));
        panel.FlyRequested += WgFlyTo;

        parent.AddChild(button);
        parent.AddChild(panel);
        WgPlaceWaypointControls(parent);
    }

    /// <summary>Puts the waypoint controls on the given plot, below the terrain toggle at its top right.</summary>
    private void WgPlaceWaypointControls(LayoutContainer parent)
    {
        if (_wgWaypointButton == null || _wgWaypointPanel == null)
            return;

        Place(_wgWaypointButton, WgWaypointKeyTop);
        Place(_wgWaypointPanel, WgWaypointPanelTop);

        void Place(Control control, float top)
        {
            if (control.Parent != parent)
                parent.AddChild(WGInstrumentTheme.Detach(control));
            control.SetPositionLast();
            LayoutContainer.SetAnchorPreset(control, LayoutContainer.LayoutPreset.TopRight);
            LayoutContainer.SetGrowHorizontal(control, LayoutContainer.GrowDirection.Begin);
            LayoutContainer.SetGrowVertical(control, LayoutContainer.GrowDirection.End);
            LayoutContainer.SetMarginLeft(control, -WgOverlayEdge);
            LayoutContainer.SetMarginRight(control, -WgOverlayEdge);
            LayoutContainer.SetMarginTop(control, top);
            LayoutContainer.SetMarginBottom(control, top);
        }
    }

    /// <summary>Styles the waypoint controls as helm instruments and moves them with the plot.</summary>
    private void WgAttachWaypointControls(LayoutContainer parent)
    {
        if (_wgWaypointButton == null || _wgWaypointPanel == null)
            return;

        _wgWaypointButton.AddStyleClass("WgCompact");
        _wgWaypointButton.MinSize = _wgWaypointButton.MaxSize = new Vector2(120, 32);
        WGInstrumentTheme.Switch(_wgWaypointButton);
        _wgWaypointPanel.UseInstrumentTheme();
        WgPlaceWaypointControls(parent);
    }

    private void WgDisposeWaypointControls()
    {
        _wgWaypointButton?.Dispose();
        _wgWaypointPanel?.Dispose();
    }

    /// <summary>Follows the grid's waypoints; a console with no grid under it has nowhere to keep them.</summary>
    private void WgUpdateWaypoints()
    {
        if (_wgWaypointButton == null || _wgWaypointPanel == null)
            return;

        var hasGrid = EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console) && console.GridUid != null;
        _wgWaypointButton.Visible = hasGrid;
        if (!hasGrid)
        {
            _wgWaypointButton.Pressed = false;
            _wgWaypointPanel.Visible = false;
            _wgWaypointPanel.CancelPick();
            return;
        }

        var waypoints = EntManager.TryGetComponent<WGWaypointsComponent>(console!.GridUid, out var stored)
            ? stored.Waypoints
            : WgNoWaypoints;
        NetEntity? map = console.MapUid is { } mapUid ? EntManager.GetNetEntity(mapUid) : null;
        _wgWaypointPanel.UpdateWaypoints(waypoints, map, _wgWaypointCanFly);
    }

    private void WgSendWaypointMessage(BoundUserInterfaceMessage message)
    {
        if (_consoleEntity is { } console && _wgWaypointKey != null)
            EntManager.System<SharedUserInterfaceSystem>().ClientSendUiMessage(console, _wgWaypointKey, message);
    }

    /// <summary>Engages the helm's autopilot, holding the ship's present heading on arrival.</summary>
    private void WgFlyTo(Vector2 position)
    {
        if (!EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console) || console.MapID == MapId.Nullspace)
            return;

        WgSendWaypointMessage(new ShuttleConsoleAutopilotPositionMessage
        {
            Coordinates = new MapCoordinates(position, console.MapID),
            Angle = _transform.GetWorldRotation(console.GridUid ?? console.Owner),
        });
    }

    /// <summary>While picking, a left click on the plot fills the waypoint form and reaches nothing else.</summary>
    private bool WgTryPickWaypoint(GUIBoundKeyEventArgs args)
    {
        if (!_wgWaypointPicking || args.Function != EngineKeyFunctions.UIClick)
            return false;

        if (WgCursorWorldPosition(args.RelativePosition) is { } position)
            _wgWaypointPanel?.SetPicked(position);
        _wgSwallowRelease = true;
        args.Handle();
        return true;
    }

    /// <summary>Consumes the release of the click a pick took.</summary>
    private bool WgTrySwallowPickRelease(GUIBoundKeyEventArgs args)
    {
        if (!_wgSwallowRelease || args.Function != EngineKeyFunctions.UIClick)
            return false;

        _wgSwallowRelease = false;
        args.Handle();
        return true;
    }

    /// <summary>Draws the grid's waypoints on this map, pinned to the plot's edge when out of view.</summary>
    private void DrawWgWaypoints(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        if (map is not { } mapUid ||
            !EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console) ||
            !EntManager.TryGetComponent<WGWaypointsComponent>(console.GridUid, out var stored))
            return;

        var mapNet = EntManager.GetNetEntity(mapUid);
        var inset = (WgWaypointMarkerSize + 2f) * UIScale;
        foreach (var waypoint in stored.Waypoints)
        {
            if (waypoint.Map != mapNet)
                continue;

            var view = Vector2.Transform(waypoint.Position, worldToView);
            // Off the scope its marker sits at the edge, untagged: a stack lists only what the scope shows.
            var onScope = WgOnScope(view);
            view = Vector2.Clamp(view, new Vector2(inset), Vector2.Max(new Vector2(inset), new Vector2(PixelSize.X, PixelSize.Y) - new Vector2(inset)));
            var size = WgWaypointMarkerSize * UIScale;
            _wgWaypointVertices[0] = view + new Vector2(0, -size);
            _wgWaypointVertices[1] = view + new Vector2(size, 0);
            _wgWaypointVertices[2] = view + new Vector2(0, size);
            _wgWaypointVertices[3] = view + new Vector2(-size, 0);
            _wgWaypointVertices[4] = _wgWaypointVertices[0];
            handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, _wgWaypointVertices, WgWaypointColor);
            if (!onScope)
                continue;

            var dimensions = handle.GetDimensions(Font, waypoint.Name, UIScale * 0.8f);
            var labelX = view.X + size + 2f * UIScale + dimensions.X > PixelSize.X
                ? view.X - size - 2f * UIScale - dimensions.X
                : view.X + size + 2f * UIScale;
            WgBeginTag(view, new Vector2(labelX, view.Y - dimensions.Y / 2f), 2, always: true);
            WgTagLine(handle, waypoint.Name, UIScale * 0.8f, WgWaypointColor);
        }
    }

    /// <summary>Writes the cursor's and the scanner's world coordinates in the plot's bottom right corner.</summary>
    private void DrawWgCoordinateReadout(DrawingHandleScreen handle)
    {
        var cursor = _isMouseInside
            ? WgCursorWorldPosition(UserInterfaceManager.MousePositionScaled.Position - GlobalPosition)
            : null;
        var scanner = WgScannerWorldPosition();
        const float scale = 0.8f;
        var bottom = PixelSize.Y - (WgOverlayEdge + (WgCockpitControls ? WgCockpitReadoutClearance : 0f)) * UIScale;
        var right = PixelSize.X - WgOverlayEdge * UIScale;

        // Out past the Lattice's hold the numbers garble, half as badly as names.
        var lattice = EntManager.System<WGLatticeViewSystem>();
        bottom = DrawLine(scanner is { } scan
            ? lattice.GarbleForViewer(Loc.GetString("wg-radar-scanner-coordinates", ("x", $"{scan.X:0.0}"), ("y", $"{scan.Y:0.0}")), 2, 0.5f)
            : Loc.GetString("wg-radar-scanner-unknown"), bottom);
        DrawLine(cursor is { } point
            ? lattice.GarbleForViewer(Loc.GetString("wg-radar-cursor-coordinates", ("x", $"{point.X:0.0}"), ("y", $"{point.Y:0.0}")), 3, 0.5f)
            : Loc.GetString("wg-radar-cursor-unknown"), bottom);

        float DrawLine(string text, float lineBottom)
        {
            var dimensions = handle.GetDimensions(Font, text, UIScale * scale);
            var position = new Vector2(right - dimensions.X, lineBottom - dimensions.Y);
            handle.DrawString(Font, position + new Vector2(1f, 1f), text, UIScale * scale, WgReadoutShadow);
            handle.DrawString(Font, position, text, UIScale * scale, WgReadoutColor);
            return position.Y;
        }
    }
}
