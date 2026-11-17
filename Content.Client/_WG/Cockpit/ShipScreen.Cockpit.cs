using System.Linq;
using System.Numerics;
using Content.Client._WG.Cockpit;
using Content.Client._WG.CombatConsole;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client._WG.Shuttles.UI;

public sealed partial class ShipScreen
{
    /// <summary>Keeps hull integrity below the flight dials.</summary>
    public Control WgCockpitHull() => new WGGlassGauge("wg-cockpit-hull", () => WGGaugeReading.Number(
        _wgHull?.HullIntegrity * 100, 0, 100, "wg-gauge-unit-percent", decimals: 1,
        tint: _wgHull?.HullIntegrity < 0.5f ? Red : Green), true) { Name = "CockpitHull", SetHeight = 44, CompactStrip = true };

    /// <summary>Provides an independent reserve strip that stays fixed below the scrolling dials.</summary>
    public Control WgCockpitFuel() => new WGCockpitFuelBank(() => _wgHull?.Fuel);

    /// <summary>Uses extra MFD width for telemetry and overlays beside the live hull display.</summary>
    public Control WgCockpitShip(WGCockpitLease lease)
    {
        var departments = WGCockpitLease.Descendants(this).OfType<CheckBox>().Single(toggle => toggle.Name == "DepartmentToggle");
        var gauges = WGCockpitLease.Descendants(this).OfType<WGGlassGauge>().ToArray();
        lease.Take(this);
        lease.Clear(this);
        // The helm centres these captions on its keys; the MFD keeps them left-aligned.
        foreach (var toggle in new[] { DamageToggle, FireToggle, PressureToggle, PowerToggle, departments })
        {
            toggle.TextAlign = Robust.Client.UserInterface.Controls.Label.AlignMode.Left;
            lease.Remember(() => toggle.TextAlign = Robust.Client.UserInterface.Controls.Label.AlignMode.Center);
        }
        var telemetry = new GridContainer { Name = "CockpitShipTelemetry", Columns = 2, HorizontalExpand = true };
        foreach (var gauge in gauges)
        {
            lease.Take(gauge);
            gauge.MinWidth = 88;
            WGCockpitInstrumentSizing.Bind(gauge, lease, 160);
            telemetry.AddChild(gauge);
        }
        Orientation = LayoutOrientation.Vertical;
        ShipView.WgCockpitInteraction(lease);
        var plot = Scope("wg-console-hull-scope", lease.Take(ShipView), Vector2.Zero);
        var overlays = new GridContainer { Name = "CockpitShipOverlays", Columns = 4, HorizontalExpand = true };
        foreach (var control in new Control[] { DamageToggle, FireToggle, PressureToggle, PowerToggle })
            overlays.AddChild(lease.Take(control));
        var mapControls = Row(lease.Take(departments), lease.Take(FitButton));
        mapControls.Name = "CockpitShipMapControls";
        var details = Column(new WGGlassReadout(lease.Take(ShipNameLabel)), telemetry, overlays, mapControls, lease.Take(TruncatedLabel, restoreVisibility: false));
        AddChild(new WGCockpitShipLayout(plot, WGCockpitMfdLayout.Details(details)));
        return this;
    }

    /// <summary>Moves announcements and alarm controls into their own MFD page.</summary>
    public Control WgCockpitAnnouncements(WGCockpitLease lease) => WGCockpitMfdLayout.Details(lease.Take(AlarmPanel));
}
