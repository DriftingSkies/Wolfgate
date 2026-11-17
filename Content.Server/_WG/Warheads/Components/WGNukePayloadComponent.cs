using Content.Shared._WG.Warheads;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Warheads.Components;

/// <summary>
/// Gives an upstream station nuke the warhead countdown, voice and detonation effects. The nuke keeps its own blast.
/// </summary>
[RegisterComponent]
public sealed partial class WGNukePayloadComponent : Component
{
    [DataField(required: true)]
    public ProtoId<WGWarheadPayloadPrototype> Payload;

    [DataField]
    public bool PlayedVoice;
}
