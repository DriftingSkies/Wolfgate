using Content.Client.Administration.Managers;
using Robust.Client.Graphics;
using Robust.Shared.Console;
using Robust.Shared.IoC;

namespace Content.Client._WF.Vehicles.Overlays;

/// <summary>Admin-only console command that toggles the doorway and exit marker overlay.</summary>
public sealed partial class VehicleEntryPointsCommand : EntitySystem
{
    private const string CommandName = "rmc_vehicle_entries";

    [Dependency] private IClientAdminManager _adminManager = default!;
    [Dependency] private IConsoleHost _console = default!;
    [Dependency] private IOverlayManager _overlays = default!;

    private VehicleEntryPointsOverlay? _overlay;

    public override void Initialize()
    {
        _overlay = new VehicleEntryPointsOverlay(EntityManager);
        _console.RegisterCommand(CommandName, ToggleOverlay);
    }

    public override void Shutdown()
    {
        _console.UnregisterCommand(CommandName);

        if (_overlay != null && _overlays.HasOverlay<VehicleEntryPointsOverlay>())
            _overlays.RemoveOverlay(_overlay);
    }

    private void ToggleOverlay(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_adminManager.IsAdmin())
        {
            shell.WriteError(Loc.GetString("wf-vehicles-debug-admin-only"));
            return;
        }

        if (_overlay == null)
            return;

        var enabled = !_overlays.HasOverlay<VehicleEntryPointsOverlay>();
        if (enabled)
            _overlays.AddOverlay(_overlay);
        else
            _overlays.RemoveOverlay(_overlay);

        shell.WriteLine(Loc.GetString(enabled ? "wf-vehicles-debug-overlay-on" : "wf-vehicles-debug-overlay-off", ("overlay", "entries")));
    }
}
