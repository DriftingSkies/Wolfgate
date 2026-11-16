using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NanoChat;

/// <summary>The NanoChat PDA app. It reads the ID card in its PDA.</summary>
[RegisterComponent, Access(typeof(NanoChatCartridgeSystem))]
public sealed partial class NanoChatCartridgeComponent : Component
{
    /// <summary>The radio channel messages ride: its telecom server, jammers and planet comms apply.</summary>
    [DataField]
    public ProtoId<RadioChannelPrototype> RadioChannel = "Common";
}
