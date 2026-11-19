using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Wolfmed.Armor;
using Content.Shared._WG.Wolfmed.Wounds;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WG.ArmorSpecialization;

/// <summary>
/// The worn-armour specializations that answer Wolfmed: wound wards, limb locks and pain dampeners, plus the ablative
/// coat that soaks burns at the armour step.
/// </summary>
public sealed partial class WGArmorSpecializationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGWoundWardComponent, InventoryRelayedEvent<WolfmedWoundRuleAttemptEvent>>(OnWoundRuleAttempt);
        SubscribeLocalEvent<WGWoundWardComponent, ExaminedEvent>(OnWardExamined);

        SubscribeLocalEvent<WGLimbLockComponent, InventoryRelayedEvent<WolfmedSeverAttemptEvent>>(OnSeverAttempt);
        SubscribeLocalEvent<WGLimbLockComponent, ExaminedEvent>(OnLimbLockExamined);

        SubscribeLocalEvent<WGPainDampenerComponent, InventoryRelayedEvent<WolfmedArmorPainReliefEvent>>(OnPainRelief);
        SubscribeLocalEvent<WGPainDampenerComponent, ExaminedEvent>(OnDampenerExamined);

        SubscribeLocalEvent<WGAblativeCoatComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnCoatDamageModify);
        SubscribeLocalEvent<WGAblativeCoatComponent, InventoryRelayedEvent<PartDamageModifyEvent>>(OnCoatPartDamageModify,
            before: [typeof(WolfmedPartArmorSystem)]);
        SubscribeLocalEvent<WGAblativeCoatComponent, ExaminedEvent>(OnCoatExamined);
    }

    /// <summary>The body wearing <paramref name="suit"/> in a clothing slot; a suit carried in hand has none.</summary>
    public EntityUid? GetWearer(EntityUid suit) =>
        _inventory.TryGetContainingSlot(suit, out _) && _inventory.TryGetContainingEntity(suit, out var wearer)
            ? wearer
            : null;

    private void OnWoundRuleAttempt(Entity<WGWoundWardComponent> ent, ref InventoryRelayedEvent<WolfmedWoundRuleAttemptEvent> args)
    {
        // A spall liner catches what would stay in anywhere on the body, not just under the plates.
        if (!ent.Comp.Anywhere.Contains(args.Args.Wound) && !WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            return;

        if (ent.Comp.Blocks.Contains(args.Args.Wound) ||
            args.Args.Rule is { } rule && ent.Comp.BlockRules.Contains(rule))
            args.Args.Cancelled = true;
        else if (ent.Comp.Hardens.TryGetValue(args.Args.Wound, out var multiplier))
            args.Args.MinDamageMultiplier = MathF.Max(args.Args.MinDamageMultiplier, multiplier);
    }

    private void OnSeverAttempt(Entity<WGLimbLockComponent> ent, ref InventoryRelayedEvent<WolfmedSeverAttemptEvent> args)
    {
        if (!WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            return;

        if (ent.Comp.Breakthrough <= FixedPoint2.Zero || args.Args.HitDamage < ent.Comp.Breakthrough)
            args.Args.Cancelled = true;
    }

    private void OnPainRelief(Entity<WGPainDampenerComponent> ent, ref InventoryRelayedEvent<WolfmedArmorPainReliefEvent> args)
    {
        args.Args.Relief += ent.Comp.Relief;
    }

    private void OnCoatDamageModify(Entity<WGAblativeCoatComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        // A wound host's burns are localized and armoured per part; they are soaked in OnCoatPartDamageModify instead.
        if (_inventory.TryGetContainingEntity(ent.Owner, out var wearer) && HasComp<WoundHostComponent>(wearer))
            return;

        args.Args.Damage = Soak(ent, args.Args.Damage);
    }

    private void OnCoatPartDamageModify(Entity<WGAblativeCoatComponent> ent, ref InventoryRelayedEvent<PartDamageModifyEvent> args)
    {
        if (WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            args.Args.Damage = Soak(ent, args.Args.Damage);
    }

    /// <summary>Soaks the coat's share of its damage group from its charge and returns the rest.</summary>
    private DamageSpecifier Soak(Entity<WGAblativeCoatComponent> ent, DamageSpecifier damage)
    {
        if (ent.Comp.Charge <= 0f || !_prototypes.TryIndex(ent.Comp.DamageGroup, out DamageGroupPrototype? group))
            return damage;

        // A single-type coat (Heat for a fire-rated suit) soaks only that type.
        IReadOnlyList<string> types = ent.Comp.DamageType is { } single ? new List<string> { single.Id } : group.DamageTypes;

        var requested = FixedPoint2.Zero;
        foreach (var type in types)
        {
            if (damage.DamageDict.TryGetValue(type, out var amount) && amount > 0)
                requested += amount * ent.Comp.Reduction;
        }

        if (requested <= 0)
            return damage;

        var share = requested.Float() > ent.Comp.Charge
            ? ent.Comp.Reduction * ent.Comp.Charge / requested.Float()
            : ent.Comp.Reduction;

        var result = new DamageSpecifier(damage);
        var absorbed = FixedPoint2.Zero;
        foreach (var type in types)
        {
            if (!result.DamageDict.TryGetValue(type, out var amount) || amount <= 0)
                continue;

            var soaked = amount * share;
            result.DamageDict[type] = amount - soaked;
            absorbed += soaked;
        }

        ent.Comp.Charge = MathF.Max(ent.Comp.Charge - absorbed.Float(), 0f);
        ent.Comp.LastHit = _timing.CurTime;
        Dirty(ent);
        return result;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer)
            return;

        // Once a second, so a recovering coat isn't sent to clients every tick.
        var now = _timing.CurTime;
        if (now < _nextCoatRecovery)
            return;
        _nextCoatRecovery = now + CoatRecoveryInterval;

        var query = EntityQueryEnumerator<WGAblativeCoatComponent>();
        while (query.MoveNext(out var uid, out var coat))
        {
            if (coat.RegenRate <= 0f || coat.Charge >= coat.MaxCharge || now < coat.LastHit + coat.RecoveryDelay)
                continue;

            coat.Charge = MathF.Min(coat.Charge + coat.RegenRate * (float) CoatRecoveryInterval.TotalSeconds, coat.MaxCharge);
            Dirty(uid, coat);
        }
    }

    private static readonly TimeSpan CoatRecoveryInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextCoatRecovery;

    private void OnWardExamined(Entity<WGWoundWardComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Examine is { } examine)
            args.PushMarkup(Loc.GetString(examine));
    }

    private void OnLimbLockExamined(Entity<WGLimbLockComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(ent.Comp.Breakthrough > FixedPoint2.Zero
            ? Loc.GetString("wg-armor-spec-limb-lock-examine", ("breakthrough", ent.Comp.Breakthrough.Int()))
            : Loc.GetString("wg-armor-spec-limb-lock-total-examine"));
    }

    private void OnDampenerExamined(Entity<WGPainDampenerComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.Examine));
    }

    private void OnCoatExamined(Entity<WGAblativeCoatComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.Examine,
            ("percent", (int) MathF.Round(ent.Comp.Charge / MathF.Max(ent.Comp.MaxCharge, 1f) * 100f))));
    }
}
