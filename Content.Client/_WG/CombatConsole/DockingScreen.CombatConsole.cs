using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class DockingScreen
{
    /// <summary>Places docking interlocks and ports beside a full-height approach display.</summary>
    public void WgRefitInstruments()
    {
        var approach = Scope("wg-console-docking-scope", DockingControl);
        var locks = Panel("wg-console-interlocks", Column(Row(FTLLockEnabledButton, FTLLockDisabledButton), UndockAllButton));
        var ports = Panel("wg-console-docking-ports", Scroll(DockPorts), true);
        var side = Column(locks, ports);
        side.MinWidth = side.MaxWidth = 260;
        side.VerticalExpand = true;
        RemoveAllChildren();
        SeparationOverride = 8;
        AddChild(approach);
        AddChild(side);
    }
}
