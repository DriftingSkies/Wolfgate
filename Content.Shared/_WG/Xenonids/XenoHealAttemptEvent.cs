namespace Content.Shared._WG.Xenonids;

[ByRefEvent]
public record struct XenoHealAttemptEvent(bool Cancelled = false);
