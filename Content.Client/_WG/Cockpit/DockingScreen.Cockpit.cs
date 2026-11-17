using System.Numerics;
using Content.Client._WG.Cockpit;
using Robust.Client.UserInterface;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class DockingScreen
{
    /// <summary>Keeps docking, port interlocks and undocking in the cockpit MFD.</summary>
    public Control WgCockpitDock(WGCockpitLease lease)
    {
        lease.Take(this);
        lease.Clear(this);
        Orientation = LayoutOrientation.Vertical;
        DockingControl.WgCockpitInteraction(lease);
        var actions = DockingControl.WgCockpitDockActions(lease);
        var plot = Scope("wg-console-docking-scope", lease.Take(DockingControl), Vector2.Zero);
        var recenter = Button("wg-cockpit-dock-recenter");
        recenter.Name = "CockpitDockRecenter";
        recenter.OnPressed += _ => DockingControl.Offset = Vector2.Zero;
        var ftlLock = Label("wg-cockpit-dock-ftl-lock", Accent);
        ftlLock.Name = "CockpitDockFtlLockLabel";
        var details = Column(recenter, actions, ftlLock, Row(lease.Take(FTLLockEnabledButton), lease.Take(FTLLockDisabledButton)),
            lease.Take(UndockAllButton), lease.Take(DockPorts));
        AddChild(WGCockpitMfdLayout.Split(plot, WGCockpitMfdLayout.Details(details), 0.8f));
        return this;
    }
}
