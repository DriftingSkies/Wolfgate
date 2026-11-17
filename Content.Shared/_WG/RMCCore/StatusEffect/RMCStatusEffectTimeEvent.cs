namespace Content.Shared._WG.RMCCore.StatusEffect;

[ByRefEvent]
public record struct RMCStatusEffectTimeEvent(string Key, TimeSpan Duration);
