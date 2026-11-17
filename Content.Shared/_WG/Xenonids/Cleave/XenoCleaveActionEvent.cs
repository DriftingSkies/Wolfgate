using Content.Shared.Actions;

namespace Content.Shared._WG.Xenonids.Cleave;

public sealed partial class XenoCleaveActionEvent : EntityTargetActionEvent
{
    [DataField]
    public bool Flings = false;
}
