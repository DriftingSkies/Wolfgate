using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenonids.JoinXeno;

[Serializable, NetSerializable]
public record JoinLarvaQueueEvent(NetEntity Hive);
