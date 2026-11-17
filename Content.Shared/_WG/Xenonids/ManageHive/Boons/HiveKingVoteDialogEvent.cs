using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenonids.ManageHive.Boons;

[Serializable, NetSerializable]
public sealed record HiveKingVoteDialogEvent(NetEntity Cocoon, NetEntity Voted);
