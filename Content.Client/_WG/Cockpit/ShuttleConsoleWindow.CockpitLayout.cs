using System.Linq;
using Content.Client._WG.Cockpit;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>Shares the console's existing command bindings with the cockpit presentation.</summary>
    public WGCockpitParts WgBuildCockpit(WGCockpitLease lease)
    {
        var (navigation, translation) = NavContainer.WgCockpitInstruments(lease);
        var instruments = Column(navigation, ShipContainer.WgCockpitHull());
        instruments.SeparationOverride = 3;
        var announcements = ShipContainer.WgCockpitAnnouncements(lease);
        var ship = ShipContainer.WgCockpitShip(lease);
        var shield = _shieldScreen.WgCockpitShield(lease);
        var shieldDetails = _shieldScreen.WgCockpitShieldDetails(lease);
        foreach (var plot in WGCockpitLease.Descendants(AccessContainer).OfType<MapGridControl>())
            plot.WgCockpitInteraction(lease);
        lease.Take(AccessContainer);
        var systems = WGCockpitMfdLayout.Details(NavContainer.WgCockpitSystems(lease));
        var pages = new (string Key, Control Content)[]
        {
            ("wg-cockpit-nav", NavContainer.WgCockpitRadar(lease)),
            ("wg-cockpit-ship", ship),
            ("wg-cockpit-map", MapContainer.WgCockpitMap(lease)),
            ("wg-cockpit-dock", DockContainer.WgCockpitDock(lease)),
            ("wg-cockpit-access", AccessContainer),
            ("wg-cockpit-shield-page", shieldDetails),
            ("wg-cockpit-mining", WgCockpitMiningPage(lease)),
            ("wg-cockpit-systems", systems),
            ("wg-cockpit-alarms", announcements),
        };
        return new WGCockpitParts(CameraBar.WgCockpitControls(lease), lease.Take(CaptureBanner, restoreVisibility: false),
            instruments, Row(ShipContainer.WgCockpitFuel(), NavContainer.WgCargoGauge("Cockpit")),
            translation, NavContainer.WgCockpitFlight(lease), shield,
            new WGCockpitTcasPanel(CollisionBanner.WgCockpitTcasReading), pages);
    }

    /// <summary>Starts strategic data when its MFD page is selected.</summary>
    public void WgCockpitPageSelected(string key)
    {
        if (key == "wg-cockpit-map")
            MapContainer.Startup();
    }

    /// <summary>Refreshes normal navigation telemetry independently of the hidden console window.</summary>
    public void WgCockpitRefresh() => NavContainer.WgCockpitRefresh();
}
