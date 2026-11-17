using Content.Shared._WG.Xenonids.Construction;
using Content.Shared._WG.Xenonids.Hive;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenonids.ManageHive.Boons;

[DataDefinition]
[ImplicitDataDefinitionForInheritors]
[Serializable, NetSerializable]
[Virtual]
public partial class HiveBoonEvent
{
    [NonSerialized]
    public EntityUid Boon;

    [NonSerialized]
    public Entity<HiveComponent> Hive;

    [NonSerialized]
    public EntityUid? Core;
}
