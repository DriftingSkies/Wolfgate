using Content.Client._WG.Cockpit;
using Robust.Client.UserInterface;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    /// <summary>Borrows the shared battery bank without duplicating command handlers or list state.</summary>
    internal Control WgBuildCockpitGunnery(WGCockpitLease lease)
    {
        var battery = lease.Take(_wgBatteryControls);
        battery.VerticalExpand = true;
        var status = lease.Take(ServerStatus);
        var refresh = lease.Take(RefreshButton);
        refresh.HorizontalExpand = false;
        refresh.SetWidth = 72;
        var body = Column(battery, Row(status, refresh));
        body.SeparationOverride = 5;
        body.HorizontalExpand = body.VerticalExpand = true;
        return body;
    }

    /// <summary>Keeps the shared memory mode consistent after a linked console update.</summary>
    internal void WgUpdateCockpitGunnery() => WgShowGroupMode();
}
