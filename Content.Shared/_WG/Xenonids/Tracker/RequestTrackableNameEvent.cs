namespace Content.Shared._WG.Xenonids.Tracker;

/// <summary>Asks a trackable entity for the name trackers show; moved out of RMC's squad leader tracker.</summary>
[ByRefEvent]
public record struct RequestTrackableNameEvent(string? Name = null, bool Handled = false);
