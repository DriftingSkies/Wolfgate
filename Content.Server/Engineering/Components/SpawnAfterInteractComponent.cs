using Robust.Shared.Prototypes;
using Content.Shared._WF.EngineCompat;

namespace Content.Server.Engineering.Components
{
    [RegisterComponent]
    public sealed partial class SpawnAfterInteractComponent : Component
    {
        [DataField("prototype", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
        public string? Prototype { get; private set; }

        [DataField("ignoreDistance")]
        public bool IgnoreDistance { get; private set; }

        [DataField("doAfter")]
        public float DoAfterTime = 0;

        [DataField("removeOnInteract")]
        public bool RemoveOnInteract = false;
    }
}
