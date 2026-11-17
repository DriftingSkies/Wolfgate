using Content.Client._WG.CombatConsole;
using Content.Shared.Shuttles.Systems;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    /// <summary>Separates the strategic plot, jump controls and destination register.</summary>
    public void WgRefitInstruments()
    {
        var plot = Scope("wg-console-strategic-scope", MapRadar);
        FTLBar.Visible = false;
        var progress = new WGGlassGauge("wg-gauge-ftl", () => WGGaugeReading.Number(
            _shuttleEntity == null || _state == FTLState.Invalid ? null : FTLBar.Value * 100, 0, 100, "wg-gauge-unit-percent"), true);
        var jump = Panel("wg-console-drive", Column(_wgDriveNotice, new WGGlassReadout(MapFTLState), progress, FTLBar,
            MapFTLButton, MapAutopilotButton, MapRebuildButton, MapBeaconsButton));
        var list = Scroll(HyperspaceDestinations);
        var register = Column(DestinationSearch, list);
        register.VerticalExpand = true;
        var destinations = Panel("wg-console-destinations", register, true);
        var side = Column(jump, destinations);
        side.MinWidth = side.MaxWidth = 260;
        side.VerticalExpand = true;
        RemoveAllChildren();
        SeparationOverride = 8;
        AddChild(plot);
        AddChild(side);
    }
}
