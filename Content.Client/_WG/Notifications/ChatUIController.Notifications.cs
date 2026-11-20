using Content.Client._WG.Notifications;
using Content.Shared._WG.Notifications;
using Content.Shared.Chat;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    private const string WgHighlightSound = "/Audio/_Goobstation/Effects/ding.ogg";
    private static readonly TimeSpan WgHighlightCooldown = TimeSpan.FromSeconds(2);

    private TimeSpan _wgNextHighlightAlert;

    /// <summary>Plays the highlight sound and flashes the window when highlighting changed someone else's message.</summary>
    private void WgOnHighlights(ChatMessage msg, string before)
    {
        if (msg.WrappedMessage == before || msg.HideChat)
            return;

        if (_player.LocalEntity is { } local && msg.SenderEntity == EntityManager.GetNetEntity(local))
            return;

        // A burst of matching messages alerts once.
        if (_timing.RealTime < _wgNextHighlightAlert)
            return;
        _wgNextHighlightAlert = _timing.RealTime + WgHighlightCooldown;

        if (_config.GetCVar(NotificationCVars.SoundHighlight))
            EntityManager.System<AudioSystem>().PlayGlobal(WgHighlightSound, Filter.Local(), false, AudioParams.Default.WithVolume(-4f));

        WGWindowFlash.Request(NotificationCVars.FlashHighlight);
    }
}
