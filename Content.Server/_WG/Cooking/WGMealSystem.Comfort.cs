using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.Cooking;

/// <summary>How comfortable someone is, 0 to 100; it only ever helps.</summary>
[RegisterComponent, Access(typeof(WGMealSystem))]
public sealed partial class WGComfortComponent : Component
{
    /// <summary>0 to 100.</summary>
    [DataField]
    public float Comfort;

    /// <summary>When they last finished a home-cooked dish, for sharing a meal; null if they haven't.</summary>
    [ViewVariables]
    public TimeSpan? LastMeal;

    /// <summary>0 for nothing to show, 1 content, 2 cozy.</summary>
    [ViewVariables]
    public int Tier;
}

public sealed partial class WGMealSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private WGCookingSystem _cooking = default!;

    private const float MealComfort = 15f;
    private const float SoupComfort = 0.3f;
    private const float SharedMealComfort = 10f;
    private const float TendedComfort = 8f;

    /// <summary>Comfort a lit hearth gives each tick to whoever is close, up to <see cref="HearthComfortCap"/>.</summary>
    private const float HearthComfort = 1f;
    private const float HearthComfortCap = 60f;
    private const float HearthReach = 2.5f;

    private const float ComfortFade = 0.5f;
    private const float ContentAt = 40f;
    private const float CozyAt = 75f;
    private const float ShareReach = 3f;
    private static readonly TimeSpan ShareWithin = TimeSpan.FromMinutes(2);

    private void InitializeComfort()
    {
        SubscribeLocalEvent<WGComfortComponent, ExaminedEvent>(OnComfortExamined);
    }

    /// <summary>Meal buffs' strength factor for someone's comfort: 1, 1.1 content, 1.2 cozy.</summary>
    public float ComfortFactor(EntityUid uid)
    {
        return CompOrNull<WGComfortComponent>(uid)?.Tier switch
        {
            2 => 1.2f,
            1 => 1.1f,
            _ => 1f,
        };
    }

    /// <summary>Raises someone's comfort, capped at <paramref name="cap"/>; only the living with a mind's body get it.</summary>
    public void AddComfort(EntityUid uid, float amount, float cap = 100f)
    {
        if (amount <= 0f || !HasComp<MobStateComponent>(uid) || _mobs.IsDead(uid))
            return;

        var comfort = EnsureComp<WGComfortComponent>(uid);
        if (comfort.Comfort >= cap)
            return;
        comfort.Comfort = Math.Min(cap, comfort.Comfort + amount);
        UpdateTier((uid, comfort));
    }

    /// <summary>A home-cooked dish finished: comfort, and more for both if someone close by ate lately too.</summary>
    private void AteHomeCooking(EntityUid eater)
    {
        AddComfort(eater, MealComfort);
        if (!TryComp<WGComfortComponent>(eater, out var comfort))
            return;

        var now = _timing.CurTime;
        var shared = false;
        foreach (var other in _lookup.GetEntitiesInRange<WGComfortComponent>(Transform(eater).Coordinates, ShareReach))
        {
            if (other.Owner == eater || !AteLately(other.Comp, now))
                continue;
            shared = true;
            AddComfort(other, SharedMealComfort);
            _popup.PopupEntity(Loc.GetString("wg-comfort-shared"), other, other);
        }

        comfort.LastMeal = now;
        if (!shared)
            return;
        AddComfort(eater, SharedMealComfort);
        _popup.PopupEntity(Loc.GetString("wg-comfort-shared"), eater, eater);
    }

    private void UpdateComfort(float seconds)
    {
        // Warmth by a lit hearth.
        var hearths = EntityQueryEnumerator<WGHearthComponent, TransformComponent>();
        while (hearths.MoveNext(out var uid, out _, out var xform))
        {
            if (!_cooking.Lit(uid))
                continue;
            foreach (var actor in _lookup.GetEntitiesInRange<ActorComponent>(xform.Coordinates, HearthReach))
                AddComfort(actor, HearthComfort, HearthComfortCap);
        }

        // It fades slowly; gone at nothing.
        _faded.Clear();
        var query = EntityQueryEnumerator<WGComfortComponent>();
        while (query.MoveNext(out var uid, out var comfort))
        {
            comfort.Comfort = Math.Max(0f, comfort.Comfort - ComfortFade * seconds / 5f);
            if (comfort.Comfort <= 0f && !AteLately(comfort, _timing.CurTime))
                _faded.Add(uid);
            else
                UpdateTier((uid, comfort));
        }

        foreach (var uid in _faded)
            RemComp<WGComfortComponent>(uid);
    }

    private readonly List<EntityUid> _faded = new();

    /// <summary>Whether they finished a home-cooked dish within <see cref="ShareWithin"/>.</summary>
    private static bool AteLately(WGComfortComponent comfort, TimeSpan now)
    {
        return comfort.LastMeal is { } last && now - last <= ShareWithin;
    }

    private void UpdateTier(Entity<WGComfortComponent> ent)
    {
        var tier = ent.Comp.Comfort >= CozyAt ? 2 : ent.Comp.Comfort >= ContentAt ? 1 : 0;
        if (tier == ent.Comp.Tier)
            return;

        // Told when it rises, never when it falls: there's nothing to warn about.
        if (tier > ent.Comp.Tier)
            _popup.PopupEntity(Loc.GetString(tier == 2 ? "wg-comfort-cozy" : "wg-comfort-content"), ent, ent);
        ent.Comp.Tier = tier;
    }

    private void OnComfortExamined(Entity<WGComfortComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Tier == 0)
            return;
        var self = args.Examiner == args.Examined;
        args.PushMarkup(Loc.GetString(ent.Comp.Tier == 2 ? "wg-comfort-examine-cozy" : "wg-comfort-examine-content",
            ("target", ent.Owner), ("self", self ? 1 : 0)));
    }
}
