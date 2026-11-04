using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Server._WF.Wolfmed.Damage;

/// <summary>Tells whether a heal would change anything, so a no-op heal can skip the damage pipeline.</summary>
public static class WolfmedHealCheck
{
    /// <summary>True when the specifier deals any damage, or lowers a type the entity has above zero.</summary>
    public static bool WouldHeal(DamageableComponent? damageable, DamageSpecifier heal)
    {
        if (damageable == null)
            return false;

        foreach (var (type, amount) in heal.DamageDict)
        {
            if (amount > FixedPoint2.Zero)
                return true;

            if (amount < FixedPoint2.Zero && damageable.Damage.DamageDict.GetValueOrDefault(type) > FixedPoint2.Zero)
                return true;
        }

        return false;
    }
}
