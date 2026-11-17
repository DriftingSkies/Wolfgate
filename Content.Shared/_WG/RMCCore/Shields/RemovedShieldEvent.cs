namespace Content.Shared._WG.RMCCore.Shields;

[ByRefEvent]
public readonly record struct RemovedShieldEvent(XenoShieldSystem.ShieldType Type);
