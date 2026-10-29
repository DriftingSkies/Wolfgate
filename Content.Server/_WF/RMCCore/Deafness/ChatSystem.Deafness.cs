using Content.Shared._EinsteinEngines.Language;
using Content.Shared._WF.RMCCore.Deafness;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    /// <summary>Share of the letters in nearby speech a deaf listener still catches, from lip reading.</summary>
    private const float WfDeafReadability = 0.25f;

    /// <summary>Whether a listener is deaf to speech from someone else in a spoken (not signed) language.</summary>
    private bool WfCantHear(EntityUid listener, EntityUid source, LanguagePrototype language)
    {
        return listener != source && language.SpeechOverride.RequireSpeech && HasComp<DeafComponent>(listener);
    }

    /// <summary>What a deaf listener makes of nearby speech: a few letters of it, and that wrapped with the speaker's name.</summary>
    private (string Message, string Wrapped) WfDeafHeard(EntityUid source, string name, string perceived, LanguagePrototype language)
    {
        var heard = ObfuscateMessageReadability(perceived, WfDeafReadability);
        return (heard, WrapPublicMessage(source, name, heard, language));
    }
}
