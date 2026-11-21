using Content.Shared._WG.Cooking;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._WG.Cooking;

/// <summary>The fighting and flying meal traits, each gentler than the stimulant it stands in for.</summary>
public sealed partial class WGMealSystem
{
    /// <summary>Most a Brawny meal adds to melee damage, and most a Steady one to a ship's thrust.</summary>
    private const float MaxBrawny = 0.15f;
    private const float MaxSteady = 0.12f;

    private void InitializeCombat()
    {
        SubscribeLocalEvent<WGWellFedComponent, GetMeleeDamageEvent>(OnMeleeDamage);
    }

    /// <summary>Brawny: about 7.5% more melee damage for an ordinary dish, 15% at most.</summary>
    private void OnMeleeDamage(Entity<WGWellFedComponent> ent, ref GetMeleeDamageEvent args)
    {
        var strength = TraitStrength(ent, WGMealTrait.Brawny);
        if (args.User != ent.Owner || strength <= 0f)
            return;
        args.Damage *= 1f + Math.Min(MaxBrawny, 0.075f * strength * ComfortFactor(ent));
    }

    /// <summary>Steady: the thrust factor a pilot's meal gives the ship they fly, about 6% for an ordinary dish, 12% at most.</summary>
    public float HelmFactor(EntityUid pilot)
    {
        var strength = TraitStrength(pilot, WGMealTrait.Steady);
        if (strength <= 0f)
            return 1f;
        return 1f + Math.Min(MaxSteady, 0.06f * strength * ComfortFactor(pilot));
    }
}
