using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._WG.Shuttles;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    /// <summary>How often open helms are checked against their ship's FTL drive, in seconds.</summary>
    private const float WgFtlDriveInterval = 0.25f;

    private float _wgFtlDriveAccumulator;

    /// <summary>The drive status of the ship a console flies, which is the ship a jump from it would move.</summary>
    private WGFtlDriveStatus GetWgFtlDriveStatus(EntityUid console)
    {
        var selected = new ConsoleShuttleEvent { Console = console };
        RaiseLocalEvent(console, ref selected);
        return TryComp(selected.Console, out TransformComponent? xform) && xform.GridUid is { } grid
            ? _shuttle.GetFtlDriveStatus(grid)
            : WGFtlDriveStatus.Missing;
    }

    /// <summary>Rebuilds a helm's state if the drive it reports is out of date; the open helm then updates itself.</summary>
    private void RefreshWgFtlDrive(EntityUid uid, ShuttleConsoleComponent console)
    {
        var status = GetWgFtlDriveStatus(uid);
        if (_ui.TryGetUiState<ShuttleBoundUserInterfaceState>(uid, ShuttleConsoleUiKey.Key, out var state) &&
            state.MapState.FtlDrive == status)
            return;

        UpdateConsoleState(uid, console);
    }

    /// <summary>Keeps every open helm current when its ship's drive is built, removed, powered or unpowered.</summary>
    private void UpdateWgFtlDrive(float frameTime)
    {
        _wgFtlDriveAccumulator += frameTime;
        if (_wgFtlDriveAccumulator < WgFtlDriveInterval)
            return;

        _wgFtlDriveAccumulator = 0;
        var query = EntityQueryEnumerator<ShuttleConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (_ui.IsUiOpen(uid, ShuttleConsoleUiKey.Key))
                RefreshWgFtlDrive(uid, console);
        }
    }
}
