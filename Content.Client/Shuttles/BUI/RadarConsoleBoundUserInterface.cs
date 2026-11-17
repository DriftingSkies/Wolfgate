using Content.Client._WG.Administration.UI.AdminRadar; // WOLFGATE(Administration)
using Content.Client.Shuttles.UI;
using Content.Shared.Shuttles.BUIStates;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using RadarConsoleWindow = Content.Client.Shuttles.UI.RadarConsoleWindow;

namespace Content.Client.Shuttles.BUI;

[UsedImplicitly]
public sealed class RadarConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private RadarConsoleWindow? _window;
    private AdminRadarWindow? _wgAdminWindow; // WOLFGATE(Administration): the scanner window an admin in a ghost gets instead

    public RadarConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        // WOLFGATE(Administration) START: an admin in a ghost gets the resizable, far-zooming scanner
        _wgAdminWindow = AdminRadarWindow.OpenFor(this);
        if (_wgAdminWindow != null)
            return;
        // WOLFGATE END
        _window = this.CreateWindow<RadarConsoleWindow>();
        _window?.SetConsole(Owner);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not NavBoundUserInterfaceState cState)
            return;

        _window?.UpdateState(cState.State);
        _wgAdminWindow?.UpdateState(cState.State); // WOLFGATE(Administration)
    }
}
