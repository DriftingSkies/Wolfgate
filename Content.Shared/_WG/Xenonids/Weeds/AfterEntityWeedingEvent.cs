namespace Content.Shared._WG.Xenonids.Weeds;

[ByRefEvent]
public readonly record struct AfterEntityWeedingEvent(EntityUid Weeds, EntityUid CoveredEntity);
