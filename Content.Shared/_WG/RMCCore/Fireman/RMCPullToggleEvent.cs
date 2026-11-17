namespace Content.Shared._WG.RMCCore.Fireman;

[ByRefEvent]
public record struct RMCPullToggleEvent(bool Handled = false);
