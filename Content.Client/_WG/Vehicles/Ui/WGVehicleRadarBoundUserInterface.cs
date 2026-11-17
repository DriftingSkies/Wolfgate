using Content.Client.Shuttles.UI;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Vehicles.Ui;

/// <summary>Hosts the driver's existing mass scanner inside the vehicle instrument panel.</summary>
public sealed class WGVehicleRadarBoundUserInterface : BoundUserInterface
{
    private RadarConsoleWindow? _window;

    public WGVehicleRadarBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new RadarConsoleWindow();
        _window.OnClose += Close;
        _window.SetConsole(Owner);
        if (EntMan.System<WGVehicleHudSystem>().TryEmbedMenu(_window, Loc.GetString("wg-vehicle-tab-radar")))
            _window.SetHeight = 240;
        else
            _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is NavBoundUserInterfaceState navigation)
            _window?.UpdateState(navigation.State);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || _window == null)
            return;
        _window.OnClose -= Close;
        _window.Close();
        _window.Dispose();
        _window = null;
    }
}
