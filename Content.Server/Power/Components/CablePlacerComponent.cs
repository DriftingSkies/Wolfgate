using Robust.Shared.Prototypes;
using Content.Shared._WF.EngineCompat;
using Content.Shared.Power;

namespace Content.Server.Power.Components
{
    [RegisterComponent]
    public sealed partial class CablePlacerComponent : Component
    {
        [DataField("cablePrototypeID", customTypeSerializer:typeof(PrototypeIdSerializer<EntityPrototype>))]
        public string? CablePrototypeId = "CableHV";

        [DataField("blockingWireType")]
        public CableType BlockingCableType = CableType.HighVoltage;
    }
}
