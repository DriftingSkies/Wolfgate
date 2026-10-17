using Robust.Shared.Prototypes;
using Content.Shared.Damage.Prototypes;
using Content.Shared._WF.EngineCompat;

namespace Content.Server.Damage.Components;

/// <summary>
///     This component shows entity damage severity when it is examined by player.
/// </summary>
[RegisterComponent]
public sealed partial class ExaminableDamageComponent : Component
{
    [DataField("messages", required: true, customTypeSerializer:typeof(PrototypeIdSerializer<ExaminableDamagePrototype>))]
    public string? MessagesProtoId;

    public ExaminableDamagePrototype? MessagesProto;
}
