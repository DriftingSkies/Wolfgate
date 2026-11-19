using Content.Shared._WG.Cooking;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._WG.Cooking;

/// <summary>The fighting and flying meal traits, each gentler than the stimulant it stands in for.</summary>
public sealed partial class WGMealSystem
{
    /// <summary>Most a Brawny meal adds to melee damage, and most a Steady one to a ship's thrust.</summary>
    private const float MaxBrawny = 0.1f;
    private const float MaxSteady = 0.08f;

    private void InitializeCombat()
    {
        SubscribeLocalEvent<WGWellFedComponent, GetMeleeDamageEvent>(OnMeleeDamage);
    }

    /// <summary>Brawny: about 5% more melee damage for an ordinary dish, 10% at most.</summary>
    private void OnMeleeDamage(Entity<WGWellFedComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (args.User != ent.Owner || !ent.Comp.Traits.Contains(WGMealTrait.Brawny))
            return;
        args.Damage *= 1f + Math.Min(MaxBrawny, 0.05f * ent.Comp.Strength * ComfortFactor(ent));
    }

    /// <summary>Steady: the thrust factor a pilot's meal gives the ship they fly, about 4% for an ordinary dish, 8% at most.</summary>
    public float HelmFactor(EntityUid pilot)
    {
        if (!TryComp<WGWellFedComponent>(pilot, out var fed) || !fed.Traits.Contains(WGMealTrait.Steady))
            return 1f;
        return 1f + Math.Min(MaxSteady, 0.04f * fed.Strength * ComfortFactor(pilot));
    }
}
