using Robust.Shared.Network;

namespace Content.Shared._WG.Xenonids.JoinXeno;

[ByRefEvent]
public record struct XenoBurstPriorityEvent(NetUserId? BurstVictimUserId, NetUserId? InfectorUserId, NetEntity? Hive, NetEntity? SpawnedLarva = null);
