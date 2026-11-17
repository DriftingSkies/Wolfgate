using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenonids.ManageHive;

[ByRefEvent]
[Serializable, NetSerializable]
public sealed record ManageHiveSacrificeBurrowedTargetEvent(NetEntity Target);
