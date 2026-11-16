namespace Content.Shared._WF.Modsuits.Modules;

/// <summary>Raised before a knockdown lands; a rolling MOD sphere cancels it.</summary>
[ByRefEvent]
public record struct KnockDownAttemptEvent(bool Cancelled = false);
