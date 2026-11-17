using Content.Shared.Actions;

namespace Content.Shared._WG.Xenonids.Aid;

public sealed partial class XenoAidActionEvent : EntityTargetActionEvent
{
    [DataField]
    public XenoAidMode aidType = XenoAidMode.Healing;
}
