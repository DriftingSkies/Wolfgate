namespace Content.Shared._WG.RMCCore.OnCollide;

[ByRefEvent]
public readonly record struct DamageCollideEvent(EntityUid Target);
