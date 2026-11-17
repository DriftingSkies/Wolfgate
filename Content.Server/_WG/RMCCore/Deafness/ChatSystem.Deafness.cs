using Content.Shared._EinsteinEngines.Language;
using Content.Shared._WG.RMCCore.Deafness;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    /// <summary>Share of the letters in nearby speech a deaf listener still catches, from lip reading.</summary>
    private const float WgDeafReadability = 0.25f;

    /// <summary>Whether a listener is deaf to speech from someone else in a spoken (not signed) language.</summary>
    private bool WgCantHear(EntityUid listener, EntityUid source, LanguagePrototype language)
    {
        return listener != source && language.SpeechOverride.RequireSpeech && HasComp<DeafComponent>(listener);
    }

    /// <summary>What a deaf listener makes of nearby speech: a few letters of it, and that wrapped with the speaker's name.</summary>
    private (string Message, string Wrapped) WgDeafHeard(EntityUid source, string name, string perceived, LanguagePrototype language, Robust.Shared.Prototypes.ProtoId<Content.Shared.Speech.SpeechVerbPrototype>? speechVerb)
    {
        var heard = ObfuscateMessageReadability(perceived, WgDeafReadability);
        return (heard, WrapPublicMessage(source, name, heard, language, speechVerb: speechVerb));
    }
}
