namespace Content.Shared._WG.Xenonids.HiveLeader;

[ByRefEvent]
public readonly record struct HiveLeaderStatusChangedEvent(bool BecameLeader);
