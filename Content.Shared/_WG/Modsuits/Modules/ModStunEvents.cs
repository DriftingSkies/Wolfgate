namespace Content.Shared._WG.Modsuits.Modules;

/// <summary>Raised before a knockdown lands; a rolling MOD sphere cancels it.</summary>
[ByRefEvent]
public record struct KnockDownAttemptEvent(bool Cancelled = false);
