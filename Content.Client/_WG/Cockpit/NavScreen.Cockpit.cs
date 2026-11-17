using System.Numerics;
using Content.Client._WG.Cockpit;
using Content.Client._WG.CombatConsole;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Physics.Components;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    /// <summary>Builds compact passive instruments while retaining the helm's existing data sources.</summary>
    public (Control Instruments, Control Translation) WgCockpitInstruments(WGCockpitLease lease)
    {
        var heading = new WGHeadingInstrument(() =>
            _entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform)
                ? (-_xformSystem.GetWorldRotation(transform)).Reduced().FlipPositive().Degrees : null)
            { Name = "CockpitHeading", MinHeight = 106, SetHeight = 106 };
        var velocity = new WGVelocityVectorInstrument(() =>
            _entManager.TryGetComponent(_shuttleEntity, out PhysicsComponent? physics) &&
            _entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform)
                ? WGCockpitVelocityReading.FromWorld(physics.LinearVelocity, _xformSystem.GetWorldRotation(transform)) : null)
            { Name = "CockpitVelocity" };
        var yaw = new WGGlassGauge("wg-gauge-yaw", () => WgMotionReading(2))
            { Name = "CockpitYaw", MinWidth = 88, SetHeight = 100, CaptionInset = 8 };
        WGCockpitInstrumentSizing.Bind(heading, lease, 176);
        WGCockpitInstrumentSizing.Bind(velocity, lease, 160);
        WGCockpitInstrumentSizing.Bind(yaw, lease, 160);
        var forward = new WGGlassGauge("wg-gauge-forward", () => WgMotionReading(1), true)
            { Name = "CockpitForward", SetHeight = 44, CompactStrip = true };
        var lateral = new WGGlassGauge("wg-gauge-lateral", () => WgMotionReading(0), true)
            { Name = "CockpitLateral", SetHeight = 44, CompactStrip = true };
        var coordinates = new WGCockpitCoordinates(WgCockpitPosition) { Name = "CockpitCoordinates" };
        var altitude = new WGGlassGauge("wg-gauge-altitude", () =>
        {
            double? value = GridAltitude.Visible && _shuttleEntity is { } shuttle ? _zLevels.GetAbsoluteAltitude(shuttle) : null;
            var limit = WGGaugeScale.Ceiling(Math.Abs(value ?? 0), 10);
            return WGGaugeReading.Number(value, -limit, limit, "wg-gauge-unit-altitude", 2);
        }, true) { Name = "CockpitAltitude", SetHeight = 44, CompactStrip = true, Visible = GridAltitude.Visible };
        var climb = new WGGlassGauge("wg-gauge-climb", () => WGGaugeReading.Number(
            GridVerticalVelocity.Visible && _entManager.TryGetComponent(_shuttleEntity, out CEZPhysicsComponent? physics)
                ? physics.Velocity : null, -10, 10, "wg-gauge-unit-climb", 2), true)
            { Name = "CockpitClimb", SetHeight = 44, CompactStrip = true, Visible = GridVerticalVelocity.Visible };
        var vertical = Row(altitude, climb);
        vertical.Visible = GridAltitude.Visible;
        Action<Control> updateAltitude = control => altitude.Visible = vertical.Visible = control.Visible;
        Action<Control> updateClimb = control => climb.Visible = control.Visible;
        GridAltitude.OnVisibilityChanged += updateAltitude;
        GridVerticalVelocity.OnVisibilityChanged += updateClimb;
        lease.Remember(() =>
        {
            GridAltitude.OnVisibilityChanged -= updateAltitude;
            GridVerticalVelocity.OnVisibilityChanged -= updateClimb;
        });
        var motion = Row(velocity, yaw);
        motion.Name = "CockpitMotionDials";
        motion.SeparationOverride = 12;
        var instruments = Column(heading, motion);
        instruments.SeparationOverride = 3;
        return (instruments, Column(Row(forward, coordinates, lateral), vertical));
    }

    /// <summary>The ship's centre of mass in world coordinates, which is the position the helm lists.</summary>
    private Vector2? WgCockpitPosition()
    {
        if (!_entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform))
            return null;
        return _entManager.TryGetComponent(_shuttleEntity, out PhysicsComponent? body)
            ? Vector2.Transform(body.LocalCenter, _xformSystem.GetWorldMatrix(transform))
            : _xformSystem.GetWorldPosition(transform);
    }

    /// <summary>Refreshes contextual flight data when the original navigation panel is hidden.</summary>
    public void WgCockpitRefresh()
    {
        if (_entManager.TryGetComponent(_shuttleEntity, out TransformComponent? transform))
        {
            UpdateAltitude(transform);
            var position = WgCockpitPosition() ?? Vector2.Zero;
            GridPosition.Text = Loc.GetString("shuttle-console-position-value", ("X", $"{position.X:0.0}"), ("Y", $"{position.Y:0.0}"));
        }
        else
            GridAltitude.Visible = GridVerticalVelocity.Visible = GridTravelState.Visible = false;
    }

    /// <summary>Moves propulsion controls into the cockpit's permanent lower bank.</summary>
    public Control WgCockpitFlight(WGCockpitLease lease)
    {
        return Column(lease.Take(DampenerModeButtons, restoreVisibility: false), lease.Take(WgOrbitButton));
    }

    /// <summary>Moves the live radar into the navigation MFD, with its contact toggles on the glass.</summary>
    public Control WgCockpitRadar(WGCockpitLease lease)
    {
        var radar = lease.Take(NavRadar);
        radar.WgCockpitInteraction(lease);
        radar.VerticalExpand = true;
        var layers = ScopeLayers(radar, Vector2.Zero);
        var keys = new WGCockpitRadarKeys(Key(IFFToggle, "shuttle-console-iff-toggle-short"),
            Key(IFFDetailedToggle, "shuttle-console-iff-detailed-short"));
        layers.AddChild(keys);
        LayoutContainer.SetAnchorAndMarginPreset(keys, LayoutContainer.LayoutPreset.Wide);
        return layers;

        Button Key(Button button, string caption)
        {
            lease.Take(button);
            var text = button.Text;
            var tooltip = button.ToolTip;
            var compact = button.HasStyleClass("WgCompact");
            lease.Remember(() =>
            {
                button.Text = text;
                button.ToolTip = tooltip;
                if (!compact)
                    button.RemoveStyleClass("WgCompact");
                Switch(button);
            });
            button.Text = Loc.GetString(caption);
            button.ToolTip = tooltip ?? text;
            button.AddStyleClass("WgCompact");
            button.HorizontalExpand = false;
            button.MinSize = button.MaxSize = WGCockpitRadarKeys.Key;
            return button;
        }
    }

    /// <summary>Keeps sensor configuration and wired auxiliary controls available without another window.</summary>
    public Control WgCockpitSystems(WGCockpitLease lease)
    {
        var speedLabel = (Label) MaximumShuttleSpeedBox.GetChild(0);
        var caption = speedLabel.Text;
        lease.Remember(() => speedLabel.Text = caption);
        speedLabel.Text = Loc.GetString("wg-cockpit-speed-limit");
        var dimension = NetworkPortsBox.LimitedDimension;
        var rows = NetworkPortsBox.Rows;
        var columns = NetworkPortsBox.Columns;
        lease.Remember(() =>
        {
            if (dimension == Dimension.Row)
                NetworkPortsBox.Rows = rows;
            else
                NetworkPortsBox.Columns = columns;
        });
        NetworkPortsBox.Columns = 2;
        return Column(new WGGlassReadout(Column(lease.Take(ShuttleDesignation), lease.Take(GridPosition))),
            lease.Take(MaximumShuttleSpeedBox), lease.Take(IffSearchBox), lease.Take(MaximumIFFDistanceBox), lease.Take(NetworkPortsBox));
    }
}
