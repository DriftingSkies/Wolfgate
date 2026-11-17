using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenonids.HiveLeader;

[Serializable, NetSerializable]
public sealed record HiveLeaderWatchEvent(NetEntity Leader);
