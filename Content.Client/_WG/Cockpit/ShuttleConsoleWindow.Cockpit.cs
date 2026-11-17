using Content.Client._WG.Cockpit;
using Content.Client._WG.CombatConsole;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    private Button? _wgCockpitButton;
    private bool _wgCockpitShipVisible;

    /// <summary>The height of the Enter Cockpit key, half as tall again as a normal console key.</summary>
    public const float WgCockpitButtonHeight = 48;

    /// <summary>
    /// Requests only the summary (hull integrity and fuel, no tile overlays) unless the detailed ship page is showing:
    /// the windowed ship page, or the cockpit's ship page while the cockpit is up.
    /// </summary>
    public bool WgHullOnly => WgCockpitActive ? !_wgCockpitShipVisible : _mode != ShuttleConsoleMode.Ship;

    /// <summary>Requests detailed ship telemetry only while its cockpit page is visible.</summary>
    public void WgCockpitShipVisible(bool visible)
    {
        if (_wgCockpitShipVisible == visible)
            return;
        _wgCockpitShipVisible = visible;
        if (WgCockpitActive)
            WgRequestShipStatus();
    }
    /// <summary>The BUI owner whose piloting session supplies the cockpit.</summary>
    public EntityUid? WgCockpitConsole { get; private set; }
    /// <summary>The console is currently presented around the world view.</summary>
    public bool WgCockpitActive { get; private set; }

    /// <summary>Builds the entry key for the end of the navigation page's left stack: full width and tall, disabled until the pilot can enter.</summary>
    private Button WgCreateCockpitButton()
    {
        _wgCockpitButton = new Button
        {
            Name = "CockpitEnter",
            Text = Loc.GetString("wg-cockpit-enter"),
            MinHeight = WgCockpitButtonHeight,
            HorizontalExpand = true,
        };
        WGInstrumentTheme.Switch(_wgCockpitButton);
        _wgCockpitButton.Disabled = true;
        _wgCockpitButton.ToolTip = Loc.GetString("wg-cockpit-seat-required");
        _wgCockpitButton.OnPressed += _ => UserInterfaceManager.GetUIController<WGCockpitUIController>().Enter(this);
        OnClose += () => UserInterfaceManager.GetUIController<WGCockpitUIController>().Exit(this);
        return _wgCockpitButton;
    }

    /// <summary>Associates the entry switch with the authoritative helm session.</summary>
    public void WgSetCockpitConsole(EntityUid console) => WgCockpitConsole = console;

    /// <summary>Keeps hull telemetry subscribed while the cockpit's permanent gauges use it.</summary>
    public void WgSetCockpitActive(bool active)
    {
        if (active && !WgCockpitActive)
            CameraBar.WgCockpitDefaultView();
        if (!active && WgCockpitActive)
        {
            CameraBar.WgCockpitRestoreView();
            // The windowed SHIP page and the next cockpit must not flash the departed session's last sweep.
            if (_mode != ShuttleConsoleMode.Ship)
                ShipContainer.ClearStatus();
        }
        WgCockpitActive = active;
        CameraBar.Visible = active || NavContainer.Visible;
        // Forced: entering or leaving the cockpit always asks for a fresh sweep, even if the request itself is unchanged.
        WgRequestShipStatus(force: true);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_wgCockpitButton != null)
            _wgCockpitButton.Disabled = !UserInterfaceManager.GetUIController<WGCockpitUIController>().CanEnter(WgCockpitConsole);
    }

#pragma warning disable CS0672, CS0618 // Dispose is still how the engine tears a control down, and this cleanup depends on it.
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            UserInterfaceManager.GetUIController<WGCockpitUIController>().Exit(this);
        base.Dispose(disposing);
    }
#pragma warning restore CS0672, CS0618
}
