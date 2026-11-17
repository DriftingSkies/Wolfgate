using Content.Shared.Damage;

namespace Content.Shared._WG.Xenonids.Stab;

[ByRefEvent]
public record struct RMCGetTailStabBonusDamageEvent(DamageSpecifier Damage);
