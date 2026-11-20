using System.Linq;
using Content.Server._WG.Farming;
using Content.Server.Body.Systems;
using Content.Server.Temperature.Systems;
using Content.Shared._WG.Cooking;
using Content.Shared._WG.Farming;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Server.Temperature.Components;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Cooking;

/// <summary>A home-cooked dish's buff, given to whoever finishes it.</summary>
[RegisterComponent]
public sealed partial class WGMealComponent : Component
{
    [DataField(required: true)]
    public List<WGMealTrait> Traits = new();

    [DataField]
    public float Strength = 1f;

    [DataField]
    public float Minutes = 10f;

    /// <summary>Preserved food (jerky, dried fish, pickles) never goes stale.</summary>
    [DataField]
    public bool NeverStale;

    /// <summary>Seconds it has stood since it was made; it goes stale over <see cref="WGMealSystem.StaleAfter"/>.</summary>
    [DataField]
    public float Age;

    /// <summary>Who is eating it, from the last bite: the food's own events name whoever holds the spoon instead.</summary>
    [ViewVariables]
    public EntityUid? Eater;

    /// <summary>The seasonings on it, at most two (<c>wgSeasoning</c> IDs).</summary>
    [DataField]
    public List<string> Seasonings = new();

    /// <summary>It was spoon-fed to someone lying down, so its buff lasts longer.</summary>
    [ViewVariables]
    public bool Tended;
}

/// <summary>Someone's one meal buff: what it does, how strong, and until when.</summary>
[RegisterComponent, Access(typeof(WGMealSystem))]
public sealed partial class WGWellFedComponent : Component
{
    [ViewVariables]
    public List<WGMealTrait> Traits = new();

    [ViewVariables]
    public float Strength = 1f;

    [ViewVariables]
    public TimeSpan Until;
}

/// <summary>Meal buffs.</summary>
public sealed partial class WGMealSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private MobStateSystem _mobs = default!;

    /// <summary>Seconds a dish stays at full strength, and by when it is fully stale (half strength).</summary>
    public const float FreshFor = 600f;
    public const float StaleAfter = 2400f;

    private static readonly float[] QualityStrength = { 1f, 1.25f, 1.5f, 2f };
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMealComponent, ConsumeDoAfterEvent>(OnBite, before: [typeof(FoodSystem), typeof(DrinkSystem)]);
        SubscribeLocalEvent<WGMealComponent, AfterFullyEatenEvent>(OnEaten);
        SubscribeLocalEvent<WGMealComponent, ExaminedEvent>(OnMealExamined);
        SubscribeLocalEvent<WGMealBuffEvent>(OnBuffEvent);
        SubscribeLocalEvent<WGWellFedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<WGWellFedComponent, ExaminedEvent>(OnFedExamined);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _seasonings = null);
        InitializeSeasoning();
        InitializeComfort();
        InitializeTending();
        InitializeCombat();
    }

    /// <summary>A dish's strength now: its own, by quality, by freshness.</summary>
    public float Strength(Entity<WGMealComponent> meal)
    {
        var quality = CompOrNull<WGProduceQualityComponent>(meal)?.Quality ?? WGCropQuality.Normal;
        return meal.Comp.Strength * QualityStrength[(int) quality] * Freshness(meal.Comp);
    }

    /// <summary>1 while fresh, easing to 0.5 once stale.</summary>
    public static float Freshness(WGMealComponent meal)
    {
        if (meal.NeverStale || meal.Age <= FreshFor)
            return 1f;
        return 1f - 0.5f * Math.Clamp((meal.Age - FreshFor) / (StaleAfter - FreshFor), 0f, 1f);
    }

    private void OnBite(Entity<WGMealComponent> ent, ref ConsumeDoAfterEvent args)
    {
        if (args.Cancelled || args.Target is not { } eater)
            return;

        ent.Comp.Eater = eater;
        if (Tend(args.User, eater))
            ent.Comp.Tended = true;
    }

    /// <summary>The buff goes to whoever ate the dish, also when someone else fed it to them.</summary>
    private void OnEaten(Entity<WGMealComponent> ent, ref AfterFullyEatenEvent args)
    {
        var eater = ent.Comp.Eater ?? args.User;
        // Spoon-fed to someone lying down, it stays with them half as long again.
        var minutes = ent.Comp.Tended ? ent.Comp.Minutes * 1.5f : ent.Comp.Minutes;
        Give(eater, ent.Comp.Traits, Strength(ent), minutes, popup: true);
        AteHomeCooking(eater);
    }

    private void OnBuffEvent(ref WGMealBuffEvent args)
    {
        Give(args.Target, args.Traits, args.Strength, args.Minutes, popup: false);
        AddComfort(args.Target, SoupComfort);
    }

    /// <summary>Gives the one meal buff, replacing any other; the same buff again is just kept up.</summary>
    public void Give(EntityUid target, List<WGMealTrait> traits, float strength, float minutes, bool popup)
    {
        if (traits.Count == 0)
            return;

        if (_mobs.IsDead(target))
            return;

        var fed = EnsureComp<WGWellFedComponent>(target);
        var trimmed = traits.Take(3).ToList();
        var same = fed.Traits.SequenceEqual(trimmed);
        var wasFast = fed.Traits.Contains(WGMealTrait.Energising) ? fed.Strength : -1f;
        fed.Traits = trimmed;
        fed.Strength = same ? Math.Max(fed.Strength, strength) : strength;
        var until = _timing.CurTime + TimeSpan.FromMinutes(minutes);
        fed.Until = same && fed.Until > until ? fed.Until : until;
        // A soup gives its buff again on every sip; only a change in the speed bonus needs the speed worked out again.
        var nowFast = fed.Traits.Contains(WGMealTrait.Energising) ? fed.Strength : -1f;
        if (!MathHelper.CloseTo(wasFast, nowFast))
            _speed.RefreshMovementSpeedModifiers(target);
        if (popup)
            _popup.PopupEntity(Loc.GetString("wg-meal-fed", ("traits", Describe(fed.Traits))), target, target);
    }

    private string Describe(List<WGMealTrait> traits)
    {
        return string.Join(", ", traits.Select(t => Loc.GetString("wg-meal-trait-" + t.ToString().ToLowerInvariant())));
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;
        _next = _timing.CurTime + Tick;
        var seconds = (float) Tick.TotalSeconds;

        // Dishes go stale, half as fast in a fridge.
        var meals = EntityQueryEnumerator<WGMealComponent>();
        while (meals.MoveNext(out var uid, out var meal))
        {
            if (!meal.NeverStale && meal.Age < StaleAfter)
                meal.Age += InFridge(uid) ? seconds * 0.5f : seconds;
        }

        _expired.Clear();
        var fed = EntityQueryEnumerator<WGWellFedComponent>();
        while (fed.MoveNext(out var uid, out var buff))
        {
            if (_timing.CurTime >= buff.Until)
            {
                _expired.Add(uid);
                continue;
            }

            // The dead get nothing from it.
            if (!_mobs.IsDead(uid))
                Apply(uid, buff, seconds);
        }

        UpdateComfort(seconds);

        // Removed before the speed refresh, or the refresh still sees the buff and keeps its speed bonus.
        foreach (var uid in _expired)
        {
            RemComp<WGWellFedComponent>(uid);
            _speed.RefreshMovementSpeedModifiers(uid);
            _popup.PopupEntity(Loc.GetString("wg-meal-worn-off"), uid, uid);
        }
    }

    private readonly List<EntityUid> _expired = new();

    private void Apply(EntityUid uid, WGWellFedComponent buff, float seconds)
    {
        // Comfort makes a good meal go further.
        var s = buff.Strength * ComfortFactor(uid);
        foreach (var trait in buff.Traits)
        {
            switch (trait)
            {
                case WGMealTrait.Hearty:
                    _hunger.ModifyHunger(uid, 0.4f * s * seconds / 5f);
                    break;
                case WGMealTrait.Fresh:
                    var heal = new DamageSpecifier();
                    heal.DamageDict["Blunt"] = -0.15f * s;
                    heal.DamageDict["Slash"] = -0.1f * s;
                    heal.DamageDict["Heat"] = -0.1f * s;
                    _damage.TryChangeDamage(uid, heal, true, false);
                    break;
                case WGMealTrait.Fortifying:
                    _blood.TryModifyBleedAmount(uid, -0.15f * s);
                    break;
                case WGMealTrait.Warming:
                case WGMealTrait.Cooling:
                    if (TryComp<TemperatureComponent>(uid, out var temp))
                    {
                        var target = 310.15f; // normal body temperature
                        var cold = temp.CurrentTemperature < target - 2f;
                        var hot = temp.CurrentTemperature > target + 2f;
                        if (trait == WGMealTrait.Warming && cold)
                            _temperature.ForceChangeTemperature(uid, temp.CurrentTemperature + 0.6f * s, temp);
                        else if (trait == WGMealTrait.Cooling && hot)
                            _temperature.ForceChangeTemperature(uid, temp.CurrentTemperature - 0.6f * s, temp);
                    }
                    break;
            }
        }
    }

    private void OnRefreshSpeed(Entity<WGWellFedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!ent.Comp.Traits.Contains(WGMealTrait.Energising))
            return;
        // Gentler than stimulants: about 5% for an ordinary dish, at most 10%.
        var bonus = 1f + Math.Min(0.1f, 0.05f * ent.Comp.Strength);
        args.ModifySpeed(bonus, bonus);
    }

    /// <summary>Whether the item sits in a fridge or freezer of some kind.</summary>
    private bool InFridge(EntityUid uid)
    {
        if (!_containers.TryGetContainingContainer(uid, out var container))
            return false;
        var id = MetaData(container.Owner).EntityPrototype?.ID ?? string.Empty;
        return id.Contains("Fridge", StringComparison.OrdinalIgnoreCase) || id.Contains("Freezer", StringComparison.OrdinalIgnoreCase);
    }

    private void OnMealExamined(Entity<WGMealComponent> ent, ref ExaminedEvent args)
    {
        var fresh = Freshness(ent.Comp);
        var key = ent.Comp.NeverStale ? "wg-meal-keeps" : ent.Comp.Age <= FreshFor * 0.5f ? "wg-meal-hot"
            : ent.Comp.Age <= FreshFor ? "wg-meal-warm" : fresh > 0.5f ? "wg-meal-cold" : "wg-meal-stale";
        args.PushMarkup(Loc.GetString(key));
        args.PushMarkup(Loc.GetString("wg-meal-gives", ("traits", Describe(ent.Comp.Traits))));
        if (ent.Comp.Seasonings.Count > 0)
            args.PushMarkup(Loc.GetString("wg-meal-seasoned", ("with", SeasoningNames(ent.Comp))));
    }

    private void OnFedExamined(Entity<WGWellFedComponent> ent, ref ExaminedEvent args)
    {
        if (args.Examiner == args.Examined)
            args.PushMarkup(Loc.GetString("wg-meal-self", ("traits", Describe(ent.Comp.Traits))));
    }
}
