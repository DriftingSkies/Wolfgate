namespace Content.Shared._WG.Xenonids.Evolution;

[ByRefEvent]
public readonly record struct XenoDevolvedEvent(EntityUid OldXeno, EntityUid NewXeno);
