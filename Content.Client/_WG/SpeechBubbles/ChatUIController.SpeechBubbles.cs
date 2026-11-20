using Content.Client.Chat.UI;
using Content.Shared._WG.SpeechBubbles;
using Content.Shared.Chat;
using Content.Shared.Mobs.Components;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    /// <summary>Whether the player's bubble settings let this message show as a bubble.</summary>
    private bool WgShowSpeechBubble(EntityUid sender, ChatMessage msg, SpeechBubble.SpeechType type)
    {
        if (type == SpeechBubble.SpeechType.Looc)
            return true;

        if (!_config.GetCVar(SpeechBubbleCVars.Enabled))
            return false;

        if (msg.Channel == ChatChannel.Dead && !_config.GetCVar(SpeechBubbleCVars.Ghosts))
            return false;

        if (type == SpeechBubble.SpeechType.Emote && !_config.GetCVar(SpeechBubbleCVars.Emotes))
            return false;

        return _config.GetCVar(SpeechBubbleCVars.NonMobs) || EntityManager.HasComponent<MobStateComponent>(sender);
    }
}
