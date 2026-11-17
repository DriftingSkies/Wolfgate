namespace Content.Shared._WG.ShipCommand;

/// <summary>Raised broadcast before an emag takes effect on <paramref name="Target"/>; cancelling it refuses the emag.</summary>
[ByRefEvent]
public record struct WGEmagAttemptEvent(EntityUid User, EntityUid Target, bool Cancelled = false);
