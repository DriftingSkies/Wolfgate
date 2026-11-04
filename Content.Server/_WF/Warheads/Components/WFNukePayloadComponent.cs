using Content.Shared._WF.Warheads;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Warheads.Components;

/// <summary>
/// Gives an upstream station nuke the warhead countdown, voice and detonation effects. The nuke keeps its own blast.
/// </summary>
[RegisterComponent]
public sealed partial class WFNukePayloadComponent : Component
{
    [DataField(required: true)]
    public ProtoId<WFWarheadPayloadPrototype> Payload;

    [DataField]
    public bool PlayedVoice;
}
