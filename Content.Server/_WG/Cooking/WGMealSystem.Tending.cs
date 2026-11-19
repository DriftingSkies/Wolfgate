using Content.Server.Nutrition.EntitySystems;
using Content.Shared._WG.Cooking;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Standing;

namespace Content.Server._WG.Cooking;

/// <summary>A bowl that soup is served in; spoon-feeding from it tends the one fed.</summary>
[RegisterComponent]
public sealed partial class WGServingComponent : Component;

/// <summary>Tending: spoon-feeding someone who is lying down.</summary>
public sealed partial class WGMealSystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    private void InitializeTending()
    {
        SubscribeLocalEvent<WGServingComponent, ConsumeDoAfterEvent>(OnServingSip, before: [typeof(FoodSystem), typeof(DrinkSystem)]);
    }

    private void OnServingSip(Entity<WGServingComponent> ent, ref ConsumeDoAfterEvent args)
    {
        if (!args.Cancelled && args.Target is { } patient)
            Tend(args.User, patient);
    }

    /// <summary>A spoonful from <paramref name="feeder"/> to <paramref name="patient"/>.</summary>
    public bool Tend(EntityUid feeder, EntityUid patient)
    {
        if (feeder == patient || !_standing.IsDown(patient) || _mobs.IsDead(patient) || !HoldsSpoon(feeder))
            return false;

        var heal = new DamageSpecifier();
        heal.DamageDict["Blunt"] = -1f;
        heal.DamageDict["Slash"] = -0.5f;
        heal.DamageDict["Heat"] = -0.5f;
        _damage.TryChangeDamage(patient, heal, true, false);

        var mend = new WGMendOrgansEvent(patient, 0.3f, new List<string>(), new List<string>(), 1f, 0.8f);
        RaiseLocalEvent(ref mend);
        AddComfort(patient, TendedComfort);

        _popup.PopupEntity(Loc.GetString("wg-tend-feeder", ("patient", patient)), patient, feeder);
        _popup.PopupEntity(Loc.GetString("wg-tend-patient", ("feeder", feeder)), patient, patient);
        return true;
    }

    private bool HoldsSpoon(EntityUid uid)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (TryComp<UtensilComponent>(held, out var utensil) && (utensil.Types & UtensilType.Spoon) != 0)
                return true;
        }

        return false;
    }
}
