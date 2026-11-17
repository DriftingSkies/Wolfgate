using System.Numerics;
using Content.Shared._WG.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map.Components;

namespace Content.Client.Shuttles.UI;

// Optional zoom toward the cursor: the point under the mouse stays put while the range eases in or out.
public partial class ShuttleNavControl
{
    private RadarModeButton? _wgZoomToMouseButton;
    private bool _wgZoomToMouse;
    private bool _wgZoomHooked;
    private Vector2? _wgZoomWorld;
    private Vector2 _wgZoomPixel;
    private Vector2 _wgZoomSetOffset;

    /// <summary>Adds the zoom-to-cursor toggle to the radar's mode buttons.</summary>
    private void AddWgZoomToMouseButton(BoxContainer buttons)
    {
        _wgZoomToMouse = _wgSettingsCfg.GetCVar(WolfgateCVars.RadarZoomToMouse);
        var button = _wgZoomToMouseButton = CreateRadarModeButton(RadarModeButtonIcon.WgZoomToMouse);
        button.ToolTip = Loc.GetString("wg-radar-zoom-to-mouse-tooltip");
        button.ToggleMode = true;
        button.Pressed = _wgZoomToMouse;
        button.OnToggled += args =>
        {
            _wgZoomToMouse = args.Pressed;
            _wgZoomWorld = null;
            _wgSettingsCfg.SetCVar(WolfgateCVars.RadarZoomToMouse, args.Pressed);
            _wgSettingsCfg.SaveToFile();
        };
        buttons.AddChild(button);
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        if (_wgZoomToMouse && (Draggable || WgCockpitControls) && _coordinates != null && MinimapScale > 0f &&
            WgZoomChangesRange(args))
        {
            // The point under the cursor, turned from the view's old rotation into the one it has once detached.
            _wgZoomPixel = args.RelativePixelPosition;
            var before = InverseMapPosition(_wgZoomPixel);
            var oldRot = WgViewRotation();
            WgDetachForPan();
            _wgZoomWorld = (-WgViewRotation()).RotateVec(oldRot.RotateVec(before));
            _wgZoomSetOffset = Offset;
            if (!_wgZoomHooked)
            {
                _wgZoomHooked = true;
                WorldRangeChanged += WgFollowZoomAnchor;
            }
        }

        base.MouseWheel(args);
    }

    /// <summary>Whether this wheel step moves the zoom; at either limit it doesn't, and the view stays on the ship.</summary>
    private bool WgZoomChangesRange(GUIMouseWheelEventArgs args)
    {
        var direction = -args.Delta.Y / ScrollSensitivity;
        var target = Math.Clamp(ActualRadarRange * MathF.Pow(ZoomFactor, direction), WorldMinRange, WorldMaxRange);
        return !target.Equals(ActualRadarRange);
    }

    /// <summary>The rotation the view is drawn in: its anchor's when following it, otherwise north-up.</summary>
    private Angle WgViewRotation()
    {
        return _angleFollow && _coordinates is { } cord ? _transform.GetWorldRotation(cord.EntityId) : Angle.Zero;
    }

    /// <summary>Re-pans each eased zoom step so the anchored point stays under the cursor.</summary>
    private void WgFollowZoomAnchor(float range)
    {
        if (_wgZoomWorld is not { } world)
            return;
        // A drag, reset or recentre moved the view since the last step; let it win.
        if (Offset != _wgZoomSetOffset || _draggin || MinimapScale <= 0f)
        {
            _wgZoomWorld = null;
            return;
        }

        var local = (_wgZoomPixel - MidPointVector) / MinimapScale;
        Offset = TargetOffset = _wgZoomSetOffset = world - new Vector2(local.X, -local.Y);
        Recentering = false;
        _wasPanned = true;
        if (range.Equals(ActualRadarRange))
            _wgZoomWorld = null;
    }

    /// <summary>Pins a ship-anchored view to the map before panning, as a right-drag does.</summary>
    private void WgDetachForPan()
    {
        if (_relativePanning || _coordinates is not { } cord ||
            !EntManager.TryGetComponent<TransformComponent>(cord.EntityId, out var xform) ||
            xform.MapUid == cord.EntityId || EntManager.HasComponent<MapComponent>(cord.EntityId))
            return;

        PanToWorld();
        _coordinates = _transform.ToCoordinates(_transform.ToMapCoordinates(cord));
        PanToEnt();
        _wasPanned = true;
    }

    public sealed partial class RadarModeButton
    {
        /// <summary>Draws the zoom-to-cursor icon: a magnifier over an arrow tip.</summary>
        private void WgDrawZoomToMouse(DrawingHandleScreen handle, Vector2 center, float radius, Color ink)
        {
            var lens = center + new Vector2(-0.2f, -0.2f) * radius;
            handle.DrawCircle(lens, radius * 0.5f, ink, false);
            handle.DrawLine(lens + new Vector2(0.35f, 0.35f) * radius, center + new Vector2(0.8f, 0.8f) * radius, ink);
            handle.DrawLine(lens + new Vector2(-0.25f, 0f) * radius, lens + new Vector2(0.25f, 0f) * radius, ink);
            handle.DrawLine(lens + new Vector2(0f, -0.25f) * radius, lens + new Vector2(0f, 0.25f) * radius, ink);
        }
    }
}
