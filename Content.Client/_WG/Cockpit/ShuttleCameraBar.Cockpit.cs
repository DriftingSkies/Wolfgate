using Content.Client._WG.Cockpit;
using Content.Shared._WG.Shuttles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client._WG.Shuttles.UI;

public sealed partial class ShuttleCameraBar
{
    private (ShuttleCameraView View, float Zoom, bool LowLight)? _wgCockpitCamera;

    /// <summary>
    /// Starts outside the ship while remembering the camera presented before entry. The cockpit session
    /// message must go first, or the server saves this request as the helm's own camera.
    /// </summary>
    public void WgCockpitDefaultView()
    {
        if (_wgCockpitCamera != null)
            return;
        if (_entManager.TryGetComponent<ShuttleCameraComponent>(_player.LocalEntity, out var camera))
            Show(camera.View, camera.Zoom, camera.LowLight);
        _wgCockpitCamera = (_view, _zoom, _lowLight);
        Request(ShuttleCameraView.External, _zoom, _lowLight);
    }

    /// <summary>Restores the camera controls while the server ends the cockpit's temporary camera session.</summary>
    public void WgCockpitRestoreView()
    {
        if (_wgCockpitCamera is not { } previous)
            return;
        _wgCockpitCamera = null;
        Show(previous.View, previous.Zoom, previous.LowLight);
    }

    /// <summary>Fits permanent camera selection and zoom above the cockpit flight controls.</summary>
    public Control WgCockpitControls(WGCockpitLease lease)
    {
        lease.Take(this);
        lease.Clear(this);
        var views = new GridContainer { Columns = 3, HorizontalExpand = true };
        var captions = new[] { "wg-cockpit-camera-helm", "wg-cockpit-camera-front", "wg-cockpit-camera-rear",
            "wg-cockpit-camera-left", "wg-cockpit-camera-right", "wg-cockpit-camera-external" };
        for (var i = 0; i < _views.Length; i++)
            views.AddChild(Compact(_views[i].Button, captions[i]));
        var night = Compact(LowLightButton, "wg-cockpit-camera-night");
        night.SetWidth = 36;
        night.HorizontalExpand = false;
        var value = lease.Take(ZoomLabel);
        value.SetWidth = 44;
        value.HorizontalExpand = false;
        var controls = Column(views, Row(night, lease.Take(ZoomSlider), value));
        controls.SeparationOverride = 4;
        AddChild(controls);
        return this;

        Button Compact(Button button, string caption)
        {
            lease.Take(button);
            var text = button.Text;
            var tooltip = button.ToolTip;
            var alignment = button.Label.Align;
            var compact = button.HasStyleClass("WgCompact");
            lease.Remember(() =>
            {
                button.Text = text;
                button.ToolTip = tooltip;
                button.Label.Align = alignment;
                if (!compact)
                    button.RemoveStyleClass("WgCompact");
                Switch(button);
            });
            button.Text = Loc.GetString(caption);
            button.ToolTip = tooltip ?? text;
            button.AddStyleClass("WgCompact");
            return button;
        }
    }
}
