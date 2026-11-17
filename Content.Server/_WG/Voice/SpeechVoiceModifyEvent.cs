using Content.Shared._WG.Voice;

namespace Content.Server._WG.Voice;

/// <summary>
/// Raised on a speaker once per line with a copy of the voice it will speak with. Change <see cref="Voice"/> and set
/// <see cref="Changed"/> to speak this line differently; the result is clamped to the creator's ranges.
/// </summary>
[ByRefEvent]
public record struct SpeechVoiceModifyEvent(SpeechVoice Voice)
{
    public bool Changed;
}
