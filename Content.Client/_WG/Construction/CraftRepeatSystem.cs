using Content.Shared._WG.Construction;

namespace Content.Client._WG.Construction;

/// <summary>
/// Sends the construction menu's repeat-craft requests.
/// </summary>
public sealed class CraftRepeatSystem : EntitySystem
{
    public void Request(string prototype, int count)
    {
        RaiseNetworkEvent(new CraftRepeatRequestEvent(prototype, count));
    }
}
