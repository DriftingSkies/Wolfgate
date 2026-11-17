namespace Content.Shared._WG.RMCCore.Melee;

[ByRefEvent]
public record struct RMCMeleeUserGetRangeEvent(EntityUid? Target, float Range);
