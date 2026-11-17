using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Client._WG.CombatConsole;
using Content.Shared._WG.Shuttles;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client._WG.Shuttles.UI;

public sealed partial class ShipScreen
{
    /// <summary>Shares of the page width taken by the hull plot, the telemetry bank and the announcement column.</summary>
    private const float WgPlotShare = 0.43f;
    private const float WgStatusShare = 0.27f;
    private const float WgSideShare = 0.3f;

    private ShipStatusSummary? _wgHull;
    private int? _wgTileCount;
    /// <summary>Gives the hull plot the widest column, with telemetry beside it and the announcements and overlay toggles stacked at the right.</summary>
    public void WgRefitInstruments()
    {
        var mapControls = ShipView.WgDetachInstrumentToolbar();
        var departments = (CheckBox) mapControls.GetChild(0);
        var overlayButtons = new[] { DamageToggle, FireToggle, PressureToggle, PowerToggle, departments };
        foreach (var button in overlayButtons)
        {
            button.Margin = new Thickness(0);
            button.AddStyleClass("WgCompact");
            button.HorizontalExpand = true;
            button.TextAlign = Robust.Client.UserInterface.Controls.Label.AlignMode.Center;
        }
        FitButton.AddStyleClass("WgCompact");
        FitButton.Margin = new Thickness(0);
        FitButton.HorizontalExpand = true;
        FitButton.MinHeight = 32;
        var toggles = Column(WgToggleRow(DamageToggle, FireToggle), WgToggleRow(PressureToggle, PowerToggle),
            WgToggleRow(departments, FitButton));
        toggles.SeparationOverride = 4;
        toggles.HorizontalExpand = true;
        var overlays = Panel("wg-console-hull-overlays", toggles, false, 8, 4);
        overlays.Name = "WgHullControls";
        var plot = Column(Scope("wg-console-hull-scope", ShipView, new Vector2(208, 150)), mapControls, NoDataLabel);
        plot.Name = "WgHullPlot";
        plot.HorizontalExpand = plot.VerticalExpand = true;
        plot.SizeFlagsStretchRatio = WgPlotShare;
        NoDataLabel.HorizontalAlignment = HAlignment.Center;
        NoDataLabel.VerticalExpand = false;
        var telemetry = ShipNameLabel.Parent!;
        telemetry.Margin = new Thickness(0);
        foreach (var control in telemetry.Children)
        {
            if (control is Label { HorizontalExpand: true } value)
                value.ClipText = true;
        }
        telemetry.Visible = false;
        var gauges = new GridContainer { Columns = 2, HorizontalExpand = true, VerticalExpand = true };
        foreach (var gauge in new[]
        {
            new WGGlassGauge("wg-gauge-hull", () => WGGaugeReading.Number(_wgHull?.WorstIntegrity * 100, 0, 100,
                "wg-gauge-unit-percent", tint: _wgHull?.WorstIntegrity < 0.5f ? Red : Green)),
            new WGGlassGauge("wg-gauge-tiles", () => WGGaugeReading.Number(_wgTileCount, 0,
                WGGaugeScale.Ceiling(_wgTileCount ?? 0, 100), "wg-gauge-unit-tiles")),
            WgHullCount("wg-gauge-damaged", () => _wgHull?.DamagedTiles),
            WgHullCount("wg-gauge-fire", () => _wgHull?.FireTiles),
            WgHullCount("wg-gauge-vented", () => _wgHull?.VentedTiles),
            WgHullCount("wg-gauge-power-loss", () => _wgHull?.UnpoweredTiles),
        })
        {
            gauge.SetHeight = float.NaN;
            gauge.MinHeight = 110;
            gauge.VerticalExpand = true;
            gauges.AddChild(gauge);
        }
        var hull = Column(new WGGlassReadout(ShipNameLabel), TruncatedLabel, gauges, telemetry);
        hull.VerticalExpand = true;
        var status = Panel("wg-console-hull-status", hull, true);
        status.Name = "WgHullStatus";
        status.SizeFlagsStretchRatio = WgStatusShare;
        AlarmPanel.WgRefitInstruments();
        var announcements = Panel("wg-console-hull-announcements", AlarmPanel, true, 8, 4);
        announcements.Name = "WgHullAnnouncements";
        var side = Column(announcements, overlays);
        side.Name = "WgHullSide";
        side.HorizontalExpand = side.VerticalExpand = true;
        side.SizeFlagsStretchRatio = WgSideShare;
        var workspace = Row(plot, status, side);
        workspace.VerticalExpand = true;
        workspace.SeparationOverride = 8;
        RemoveAllChildren();
        Orientation = LayoutOrientation.Vertical;
        VerticalExpand = true;
        SeparationOverride = 8;
        AddChild(workspace);
    }

    /// <summary>Pairs two controls on one row of the overlay panel.</summary>
    private static BoxContainer WgToggleRow(Control left, Control right)
    {
        var row = Row(left, right);
        row.SeparationOverride = 4;
        return row;
    }

    private WGGlassGauge WgHullCount(string caption, Func<int?> read) => new(caption, () =>
    {
        var count = read();
        return WGGaugeReading.Number(count, 0, Math.Max(1, _wgTileCount ?? WGGaugeScale.Ceiling(count ?? 0)),
            "wg-gauge-unit-tiles", tint: count > 0 ? Red : Green);
    });
}
