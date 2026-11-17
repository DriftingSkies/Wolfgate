using Content.Shared._WG.EmoteVoices;
using Content.Shared._WG.Genitals.Profile;
using Content.Shared._WG.Voice;
using Content.Shared.Speech;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Cloning;

/// <summary>Per-character data a genetic record keeps, since the body may be gone by the time it is cloned.</summary>
public sealed class WGCloneTemplate
{
    public SpeechVoice? Voice;

    public List<ProtoId<EmoteVoicePrototype>>? EmoteVoices;

    public bool HasSpeech;

    public ProtoId<SpeechSoundsPrototype>? SpeechSounds;

    public bool HasGenitals;

    public GenitalProfile? Genitals;
}
