using Content.Shared._WG.AdminObserver;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    /// <summary>Whether the local player is a manifested admin observer, which speaks as the living do.</summary>
    private bool WGManifested => _player.LocalEntity is { } local && _ent.HasComponent<WGManifestedComponent>(local);

    /// <summary>Works out again which channels the player may use, as when their observer manifests or hides.</summary>
    public void WGRefreshChannelPermissions()
    {
        UpdateChannelPermissions();
    }
}
