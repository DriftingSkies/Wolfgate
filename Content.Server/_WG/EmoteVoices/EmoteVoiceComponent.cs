using Content.Shared._WG.EmoteVoices;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.EmoteVoices;

/// <summary>The emote voices a character chose, laid over their species' emote sounds.</summary>
[RegisterComponent]
[Access(typeof(EmoteVoiceSystem))]
public sealed partial class EmoteVoiceComponent : Component
{
    /// <summary>The chosen voices, laid over the species' emote sounds.</summary>
    [DataField]
    public List<ProtoId<EmoteVoicePrototype>> Voices = new();
}
