using System.Threading;
using Robust.Shared.Prototypes;
using Content.Shared._WF.EngineCompat;

namespace Content.Server.Engineering.Components
{
    [RegisterComponent]
    public sealed partial class DisassembleOnAltVerbComponent : Component
    {
        [DataField("prototype", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
        public string? Prototype { get; private set; }

        [DataField("doAfter")]
        public float DoAfterTime = 0;
    }
}
