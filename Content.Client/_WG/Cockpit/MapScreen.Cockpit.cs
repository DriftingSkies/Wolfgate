using System.Numerics;
using Content.Client._WG.Cockpit;
using Content.Client._WG.CombatConsole;
using Content.Shared.Shuttles.Systems;
using Robust.Client.UserInterface;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    /// <summary>Fits strategic travel and destination selection into the expandable MFD.</summary>
    public Control WgCockpitMap(WGCockpitLease lease)
    {
        lease.Take(this);
        lease.Clear(this);
        Orientation = LayoutOrientation.Vertical;
        MapRadar.WgCockpitInteraction(lease);
        var plot = Scope("wg-console-strategic-scope", lease.Take(MapRadar), Vector2.Zero);
        var details = Column(lease.Take(MapFTLState), new WGGlassGauge("wg-gauge-ftl", () => WGGaugeReading.Number(
            _shuttleEntity == null || _state == FTLState.Invalid ? null : FTLBar.Value * 100, 0, 100, "wg-gauge-unit-percent"), true),
            lease.Take(MapFTLButton), lease.Take(MapAutopilotButton), lease.Take(MapRebuildButton),
            lease.Take(MapBeaconsButton), lease.Take(DestinationSearch), lease.Take(HyperspaceDestinations));
        // The drive notice sits above the plot, outside the scrolling details, so it can't be scrolled out of sight.
        AddChild(lease.Take(_wgDriveNotice, restoreVisibility: false));
        AddChild(WGCockpitMfdLayout.Split(plot, WGCockpitMfdLayout.Details(details)));
        return this;
    }
}
