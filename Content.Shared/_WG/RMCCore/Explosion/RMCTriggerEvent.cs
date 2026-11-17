namespace Content.Shared._WG.RMCCore.Explosion;

[ByRefEvent]
public record struct RMCTriggerEvent(EntityUid? User, bool Handled);
