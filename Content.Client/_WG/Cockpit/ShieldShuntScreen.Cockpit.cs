using System.Numerics;
using System.Linq;
using Content.Client._WG.CombatConsole;
using Content.Shared._WG.ShipShields;
using Content.Client._WG.Cockpit;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using InstrumentTheme = Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client._WG.ShipShields;

public sealed partial class WGShipShieldShuntScreen : IWGCockpitOptionalPage
{
    /// <summary>The cockpit shows shield controls only while the ship has a shield generator.</summary>
    bool IWGCockpitOptionalPage.Available => _state is { Available: true };

    private Action<WGShipShieldGeneratorStats>? _wgCockpitShowStats;
    private bool _wgCockpitCompact;
    /// <summary>Retains the live shield dial, deployment and power slider in the permanent HUD.</summary>
    public Control WgCockpitShield(WGCockpitLease lease)
    {
        lease.Take(this);
        lease.Clear(this);
        Visible = true;
        var dial = lease.Take(_dial);
        dial.SetSize = new Vector2(96);
        dial.HorizontalExpand = false;
        var allocation = lease.Take(_amount);
        allocation.Visible = true;
        var arc = lease.Take(_arc);
        var originalName = arc.Name;
        arc.Name = "CockpitShieldArc";
        _wgCockpitCompact = true;
        lease.Remember(() =>
        {
            arc.Name = originalName;
            _wgCockpitCompact = false;
        });
        var controls = InstrumentTheme.Column(lease.Take(_enabled),
            InstrumentTheme.Row(InstrumentTheme.Label("wg-cockpit-shield-arc"), arc),
            allocation, lease.Take(_concentration), lease.Take(_health));
        controls.HorizontalExpand = true;
        // The thin shield panel is sized from this column, so it fixes the narrowest usable power slider.
        controls.MinWidth = 150;
        _health.MinHeight = 12;
        AddChild(InstrumentTheme.Row(dial, controls));
        return this;
    }

    /// <summary>Exposes detailed allocation, arc, recovery and generator statistics in the MFD.</summary>
    public Control WgCockpitShieldDetails(WGCockpitLease lease)
    {
        var telemetry = InstrumentTheme.Column();
        telemetry.HorizontalExpand = true;
        var dials = new GridContainer { Columns = 2, HorizontalExpand = true };
        foreach (var gauge in WGCockpitLease.Descendants(_settings).OfType<WGGlassGauge>().ToArray())
        {
            lease.Take(gauge);
            gauge.MinWidth = 88;
            if (gauge.Strip)
            {
                gauge.SetHeight = 64;
                telemetry.AddChild(gauge);
            }
            else
            {
                WGCockpitInstrumentSizing.Bind(gauge, lease, 160);
                dials.AddChild(gauge);
            }
        }
        telemetry.AddChild(dials);
        var stats = new WGShipShieldStatsWindow();
        var specifications = stats.Contents.GetChild(0).GetChild(0);
        specifications.Parent!.RemoveChild(specifications);
        specifications.Visible = false;
        _wgCockpitShowStats = data =>
        {
            stats.UpdateStats(data);
            specifications.Visible = !specifications.Visible;
        };
        lease.Remember(() =>
        {
            _wgCockpitShowStats = null;
            if (!stats.Disposed)
                stats.Orphan();
        });
        return new WGCockpitOptionalHost(this, WGCockpitMfdLayout.Details(InstrumentTheme.Column(telemetry, InstrumentTheme.Label("wg-console-shield-vector"),
            InstrumentTheme.Label("wg-shield-helm-bearing"), lease.Take(_direction),
            lease.Take(_stats), lease.Take(_status), lease.Take(_recovery, restoreVisibility: false),
            lease.Take(_draft), lease.Take(_reset), lease.Take(_unprotected, restoreVisibility: false), specifications)));
    }
}
