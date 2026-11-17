namespace Content.Shared._WG.RMCCore.Pulling;

[ByRefEvent]
public record struct PullSlowdownAttemptEvent(EntityUid Target, bool Cancelled = false);
