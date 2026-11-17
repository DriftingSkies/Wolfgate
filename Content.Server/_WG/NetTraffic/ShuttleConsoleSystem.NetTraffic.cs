using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    /// <summary>Builds and sets the helm state of one console, for the viewer who has just opened it.</summary>
    public void WgRefreshConsole(EntityUid uid)
    {
        DockingInterfaceState? dockState = null;
        UpdateState(uid, ref dockState);
    }

    /// <summary>Removes the helm state from a console nobody has open any more.</summary>
    private void WgDropClosedHelmState(EntityUid uid)
    {
        if (!_ui.IsUiOpen(uid, ShuttleConsoleUiKey.Key))
            _ui.SetUiState(uid, ShuttleConsoleUiKey.Key, null);
    }
}
