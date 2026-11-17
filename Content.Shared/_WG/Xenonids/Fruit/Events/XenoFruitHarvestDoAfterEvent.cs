using Content.Shared._WG.Xenonids.Fruit.Components;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenonids.Fruit.Events;

[Serializable, NetSerializable]
public sealed partial class XenoFruitHarvestDoAfterEvent : DoAfterEvent
{
    public override DoAfterEvent Clone()
    {
        return this;
    }
}
