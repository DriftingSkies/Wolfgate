using Content.Shared._WG.Synthetics;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Synthetics;

/// <summary>Client end of the chassis panel; the server registers it by this class name.</summary>
[UsedImplicitly]
public sealed class WGChassisPanelBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    /// <summary>The tab the player last had open, so the panel reopens on it.</summary>
    private static int _lastTab;

    [ViewVariables]
    private WGChassisPanelWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WGChassisPanelWindow>();
        _window.OnIsolate += (part, isolate) => SendMessage(new WGChassisIsolatePartMessage(part, isolate));
        _window.OnDetach += part => SendMessage(new WGChassisDetachPartMessage(part));
        _window.OnDisconnect += organ => SendMessage(new WGChassisDisconnectOrganMessage(organ));
        _window.OnToggleBay += () => SendMessage(new WGChassisToggleBayMessage());
        _window.OnReboot += () => SendMessage(new WGChassisRebootMessage());
        _window.OnOverclock += on => SendMessage(new WGChassisOverclockMessage(on));

        _window.CurrentTab = _lastTab;
        _window.OnTabChanged += tab => _lastTab = tab;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WGChassisPanelState panel)
            _window?.Update(panel);
    }
}
