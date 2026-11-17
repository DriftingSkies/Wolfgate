using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenonids.ManageHive.Boons;

[ByRefEvent]
[Serializable, NetSerializable]
public sealed record ManageHiveActivateBoonsChosenEvent(EntProtoId<HiveBoonDefinitionComponent> Boon);
