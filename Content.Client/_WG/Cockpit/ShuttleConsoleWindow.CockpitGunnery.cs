using Content.Shared._WG.Cockpit;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>Sends cockpit gunnery requests through this helm's bound interface.</summary>
    public event Action<BoundUserInterfaceMessage>? WgCockpitGunneryCommand;

    /// <summary>Delivers the server's current reachable gunnery console to the active cockpit.</summary>
    public event Action<WGCockpitGunneryStateMessage>? WgCockpitGunneryUpdated;

    /// <summary>Forwards a cockpit command without opening a second console window.</summary>
    public void WgSendCockpitGunnery(BoundUserInterfaceMessage message) => WgCockpitGunneryCommand?.Invoke(message);

    /// <summary>Updates only the optional gun bank without replacing navigation state.</summary>
    public void WgReceiveCockpitGunnery(WGCockpitGunneryStateMessage message) => WgCockpitGunneryUpdated?.Invoke(message);
}
