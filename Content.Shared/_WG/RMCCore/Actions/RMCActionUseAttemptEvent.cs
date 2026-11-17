namespace Content.Shared._WG.RMCCore.Actions;

[ByRefEvent]
public record struct RMCActionUseAttemptEvent(EntityUid User, EntityUid? Target, bool Cancelled = false);
