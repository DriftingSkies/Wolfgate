using System.Threading;
using Content.Shared.Tools;
using Content.Shared._WF.EngineCompat;

namespace Content.Shared.Nyanotrasen.Digging;

[RegisterComponent]
public sealed partial class EarthDiggingComponent : Component
{
    [ViewVariables]
    [DataField("toolComponentNeeded")]
    public bool ToolComponentNeeded = true;

    [ViewVariables]
    [DataField("qualityNeeded", customTypeSerializer:typeof(PrototypeIdSerializer<ToolQualityPrototype>))]
    public string QualityNeeded = "Digging";

    [ViewVariables]
    [DataField("delay")]
    public float Delay = 2f;

}
