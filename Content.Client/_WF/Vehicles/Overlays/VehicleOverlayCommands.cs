using Content.Client._WF.Vehicles.Grid;
using Content.Client.Administration.Managers;
using Robust.Client.Graphics;
using Robust.Shared.Console;
using Robust.Shared.IoC;

namespace Content.Client._WF.Vehicles.Overlays;

/// <summary>Admin-only console commands that toggle the vehicle debug overlays.</summary>
public sealed partial class VehicleOverlayCommands : EntitySystem
{
    [Dependency] private IClientAdminManager _adminManager = default!;
    [Dependency] private IConsoleHost _console = default!;
    [Dependency] private GridVehicleMoverSystem _vehicleMover = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;

    private VehicleHardpointDebugOverlay? _hardpointOverlay;

    public override void Initialize()
    {
        _console.RegisterCommand("rmc_vehicle_debug", ToggleDebug);
        _console.RegisterCommand("rmc_vehicle_hardpoint", ToggleHardpoint);
        _console.RegisterCommand("rmc_vehicle_collision", ToggleCollision);
        _console.RegisterCommand("rmc_vehicle_movement", ToggleMovement);
    }

    public override void Shutdown()
    {
        if (_hardpointOverlay != null && _overlayManager.HasOverlay<VehicleHardpointDebugOverlay>())
            _overlayManager.RemoveOverlay(_hardpointOverlay);

        _console.UnregisterCommand("rmc_vehicle_debug");
        _console.UnregisterCommand("rmc_vehicle_hardpoint");
        _console.UnregisterCommand("rmc_vehicle_collision");
        _console.UnregisterCommand("rmc_vehicle_movement");
    }

    private void ToggleDebug(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_adminManager.IsAdmin())
        {
            shell.WriteError(Loc.GetString("wf-vehicles-debug-admin-only"));
            return;
        }

        var enabled = _vehicleMover.ToggleDebugOverlay();
        shell.WriteLine(Loc.GetString(enabled ? "wf-vehicles-debug-overlay-on" : "wf-vehicles-debug-overlay-off", ("overlay", "debug")));
    }

    private void ToggleHardpoint(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_adminManager.IsAdmin())
        {
            shell.WriteError(Loc.GetString("wf-vehicles-debug-admin-only"));
            return;
        }

        _hardpointOverlay ??= new VehicleHardpointDebugOverlay(EntityManager);
        _hardpointOverlay.Enabled = !_hardpointOverlay.Enabled;

        var hasOverlay = _overlayManager.HasOverlay<VehicleHardpointDebugOverlay>();
        if (_hardpointOverlay.Enabled && !hasOverlay)
            _overlayManager.AddOverlay(_hardpointOverlay);
        else if (!_hardpointOverlay.Enabled && hasOverlay)
            _overlayManager.RemoveOverlay(_hardpointOverlay);

        shell.WriteLine(Loc.GetString(_hardpointOverlay.Enabled ? "wf-vehicles-debug-overlay-on" : "wf-vehicles-debug-overlay-off", ("overlay", "hardpoint")));
    }

    private void ToggleCollision(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_adminManager.IsAdmin())
        {
            shell.WriteError(Loc.GetString("wf-vehicles-debug-admin-only"));
            return;
        }

        var enabled = _vehicleMover.ToggleCollisionOverlay();
        shell.WriteLine(Loc.GetString(enabled ? "wf-vehicles-debug-overlay-on" : "wf-vehicles-debug-overlay-off", ("overlay", "collision")));
    }

    private void ToggleMovement(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_adminManager.IsAdmin())
        {
            shell.WriteError(Loc.GetString("wf-vehicles-debug-admin-only"));
            return;
        }

        var enabled = _vehicleMover.ToggleMovementOverlay();
        shell.WriteLine(Loc.GetString(enabled ? "wf-vehicles-debug-overlay-on" : "wf-vehicles-debug-overlay-off", ("overlay", "movement")));
    }
}
