using Content.Shared.Damage;

namespace Content.Shared._WG.RMCCore.Damage;

[ByRefEvent]
public record struct DamageStateCritBeforeDamageEvent(DamageSpecifier Damage);
