using System.Linq;
using Content.Shared._WG.Cooking;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffect;

namespace Content.Server._WG.Cooking;

/// <summary>Someone's one cup buff, from a hot drink brewed in a kettle; it sits beside any meal buff.</summary>
[RegisterComponent, Access(typeof(WGMealSystem))]
public sealed partial class WGWarmCupComponent : Component
{
    [ViewVariables]
    public List<WGMealTrait> Traits = new();

    [ViewVariables]
    public float Strength = 1f;

    [ViewVariables]
    public TimeSpan Until;
}

/// <summary>The cup slot: hot drinks, their traits, and sharing a pot.</summary>
public sealed partial class WGMealSystem
{
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private StatusEffectsSystem _status = default!;

    /// <summary>Soothed: comfort builds this many times as fast, and fades this share as fast.</summary>
    private const float SoothedGain = 1.5f;
    private const float SoothedFade = 0.5f;

    /// <summary>Most of an injury's slowdown a Numbed cup takes away, and most of a blow's stamina a Steeled one.</summary>
    private const float MaxNumbed = 0.3f;
    private const float MaxSteeled = 0.15f;

    private static readonly List<string> Lungs = new() { "lungs" };
    private static readonly List<string> NoOrgans = new();

    private void InitializeCup()
    {
        SubscribeLocalEvent<WGWarmCupComponent, RefreshMovementSpeedModifiersEvent>(OnCupRefreshSpeed);
        SubscribeLocalEvent<WGWarmCupComponent, ModifySlowOnDamageSpeedEvent>(OnCupSlowOnDamage);
        SubscribeLocalEvent<WGWarmCupComponent, BeforeStaminaDamageEvent>(OnCupStaminaDamage);
        SubscribeLocalEvent<WGWarmCupComponent, ExaminedEvent>(OnCupExamined);
    }

    /// <summary>Gives the one cup buff, replacing any other cup's; the same drink again is just kept up.</summary>
    public void GiveCup(EntityUid target, List<WGMealTrait> traits, float strength, float minutes)
    {
        if (traits.Count == 0 || _mobs.IsDead(target))
            return;

        var wasFast = TraitStrength(target, WGMealTrait.Energising);
        var wasNumbed = TraitStrength(target, WGMealTrait.Numbed);
        var cup = EnsureComp<WGWarmCupComponent>(target);
        var trimmed = traits.Take(3).ToList();
        var same = cup.Traits.SequenceEqual(trimmed);
        cup.Traits = trimmed;
        cup.Strength = same ? Math.Max(cup.Strength, strength) : strength;
        var until = _timing.CurTime + TimeSpan.FromMinutes(minutes);
        cup.Until = same && cup.Until > until ? cup.Until : until;

        if (!MathHelper.CloseTo(wasFast, TraitStrength(target, WGMealTrait.Energising))
            || !MathHelper.CloseTo(wasNumbed, TraitStrength(target, WGMealTrait.Numbed)))
            _speed.RefreshMovementSpeedModifiers(target);

        // Told once, on the first sip of a new drink, not on every sip after.
        if (!same)
            _popup.PopupEntity(Loc.GetString("wg-cup-fed", ("traits", Describe(cup.Traits))), target, target);
    }

    /// <summary>The traits only hot drinks give, each tick.</summary>
    private void ApplyCupTrait(EntityUid uid, WGMealTrait trait, float s)
    {
        switch (trait)
        {
            case WGMealTrait.WideAwake:
                _status.TryRemoveStatusEffect(uid, "Drowsiness");
                // Eased directly: healing through TakeStaminaDamage jitters anyone still past half.
                if (TryComp<StaminaComponent>(uid, out var stamina) && stamina.StaminaDamage > 0f && !stamina.Critical)
                {
                    stamina.StaminaDamage = MathF.Max(0f, stamina.StaminaDamage - 3f * s);
                    Dirty(uid, stamina);
                }
                break;
            case WGMealTrait.ClearHeaded:
                var cleanse = new DamageSpecifier();
                cleanse.DamageDict["Poison"] = -0.2f * s;
                _damage.TryChangeDamage(uid, cleanse, true, false);
                break;
            case WGMealTrait.BreatheEasy:
                var breath = new DamageSpecifier();
                breath.DamageDict["Asphyxiation"] = -0.4f * s;
                _damage.TryChangeDamage(uid, breath, true, false);
                var mend = new WGMendOrgansEvent(uid, 0.1f * s, Lungs, NoOrgans, 1f, 0.8f);
                RaiseLocalEvent(ref mend);
                break;
        }
    }

    private void OnCupRefreshSpeed(Entity<WGWarmCupComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        // With a meal buff too, the meal's handler applies the stronger of the two.
        if (!HasComp<WGWellFedComponent>(ent))
            ApplyEnergising(ent, args);
    }

    /// <summary>Numbed: an injury slows you a share less, about a fifth for an ordinary cup.</summary>
    private void OnCupSlowOnDamage(Entity<WGWarmCupComponent> ent, ref ModifySlowOnDamageSpeedEvent args)
    {
        var strength = TraitStrength(ent, WGMealTrait.Numbed);
        if (strength <= 0f)
            return;
        var ease = Math.Min(MaxNumbed, 0.2f * strength * ComfortFactor(ent));
        args.Speed = 1f - (1f - args.Speed) * (1f - ease);
    }

    /// <summary>Steeled: a blow takes a little less stamina, about a tenth for an ordinary cup.</summary>
    private void OnCupStaminaDamage(Entity<WGWarmCupComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        var strength = TraitStrength(ent, WGMealTrait.Steeled);
        if (args.Value <= 0f || strength <= 0f)
            return;
        args.Value *= 1f - Math.Min(MaxSteeled, 0.1f * strength * ComfortFactor(ent));
    }

    private void OnCupExamined(Entity<WGWarmCupComponent> ent, ref ExaminedEvent args)
    {
        if (args.Examiner == args.Examined)
            args.PushMarkup(Loc.GetString("wg-cup-self", ("traits", Describe(ent.Comp.Traits))));
    }

    /// <summary>A sip of a hot drink counts as a meal for sharing: drunk near someone who ate or drank lately, both feel it, once in a while.</summary>
    private void ShareCup(EntityUid drinker)
    {
        if (!TryComp<WGComfortComponent>(drinker, out var comfort))
            return;

        var now = _timing.CurTime;
        comfort.LastMeal = now;
        if (SharedLately(comfort, now))
            return;

        var shared = false;
        foreach (var other in _lookup.GetEntitiesInRange<WGComfortComponent>(Transform(drinker).Coordinates, ShareReach))
        {
            if (other.Owner == drinker || !AteLately(other.Comp, now) || SharedLately(other.Comp, now))
                continue;
            shared = true;
            other.Comp.LastShared = now;
            AddComfort(other, SharedMealComfort);
            _popup.PopupEntity(Loc.GetString("wg-comfort-shared-cup"), other, other);
        }

        if (!shared)
            return;
        comfort.LastShared = now;
        AddComfort(drinker, SharedMealComfort);
        _popup.PopupEntity(Loc.GetString("wg-comfort-shared-cup"), drinker, drinker);
    }

    private static bool SharedLately(WGComfortComponent comfort, TimeSpan now)
    {
        return comfort.LastShared is { } last && now - last <= ShareWithin;
    }
}
