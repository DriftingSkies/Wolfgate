namespace Content.Shared._WG.NpcCrew;

/// <summary>Raised on someone as another starts to strip them or put something on them.</summary>
[ByRefEvent]
public readonly record struct WGStripAttemptEvent(EntityUid User, bool Stealth);
