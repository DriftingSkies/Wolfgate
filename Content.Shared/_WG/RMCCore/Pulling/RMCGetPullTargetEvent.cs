namespace Content.Shared._WG.RMCCore.Pulling;

[ByRefEvent]
public record struct RMCGetPullTargetEvent(EntityUid User, EntityUid Target);
