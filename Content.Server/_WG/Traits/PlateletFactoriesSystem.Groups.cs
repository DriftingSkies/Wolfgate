using Content.Shared._WG.Wolfmed.Compat;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Traits.Physical;

public sealed partial class PlateletFactoriesSystem
{
    [Dependency] private IPrototypeManager _wgPrototypes = default!;
    [Dependency] private WolfmedDamageableSystem _wgDamage = default!;

    private readonly HashSet<string> _wgCounted = new();

    /// <summary>
    /// Heals each damage group's total from the specifier evenly across the damage of that group actually present.
    /// Applying the specifier as loaded split a small group heal over every type and truncated it, healing a Heat-only
    /// body a tenth of its budget and nothing at all in crit.
    /// </summary>
    private void HealByGroup(EntityUid uid, DamageSpecifier perInterval, float multiplier, DamageableComponent damage)
    {
        _wgCounted.Clear();
        foreach (var group in _wgPrototypes.EnumeratePrototypes<DamageGroupPrototype>())
        {
            var amount = FixedPoint2.Zero;
            foreach (var type in group.DamageTypes)
            {
                if (_wgCounted.Add(type) && perInterval.DamageDict.TryGetValue(type, out var value) && value < 0)
                    amount += value;
            }

            if (amount < 0)
                _wgDamage.HealEvenly((uid, damage), amount * multiplier, group.ID);
        }
    }
}
