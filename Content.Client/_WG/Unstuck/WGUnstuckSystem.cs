using Content.Shared._WG.Unstuck;

namespace Content.Client._WG.Unstuck;

/// <summary>
/// Sends the game menu's Unstick me request to the server.
/// </summary>
public sealed class WGUnstuckSystem : EntitySystem
{
    /// <summary>Asks the server to free the local player from whatever they are stuck in.</summary>
    public void RequestUnstuck()
    {
        RaiseNetworkEvent(new WGUnstuckRequestEvent());
    }
}
