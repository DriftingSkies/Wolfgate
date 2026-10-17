using Content.Shared.Tag;
using Content.Shared._WF.EngineCompat;

namespace Content.Shared.Mind.Components;

[RegisterComponent]
public sealed partial class TransferMindOnGibComponent : Component
{
    [DataField("targetTag", customTypeSerializer: typeof(PrototypeIdSerializer<TagPrototype>))]
    public string TargetTag = "MindTransferTarget";
}
