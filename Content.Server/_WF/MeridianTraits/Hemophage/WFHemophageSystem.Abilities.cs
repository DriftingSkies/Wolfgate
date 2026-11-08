using System.Linq;
using Content.Server._WF.Wolfmed.Stasis;
using Content.Server.Body.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.MeridianTraits.Hemophage;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.MeridianTraits.Hemophage;

public sealed partial class WFHemophageSystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private WolfmedStasisSystem _stasis = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private static readonly ProtoId<DamageGroupPrototype> Brute = "Brute";
    private static readonly ProtoId<DamageGroupPrototype> Burn = "Burn";
    private static readonly ProtoId<DamageTypePrototype> Asphyxiation = "Asphyxiation";
    private static readonly TimeSpan DormantDelay = TimeSpan.FromSeconds(3);

    /// <summary>Chance per second that blood's warmth eases each bleeding wound.</summary>
    private const float WarmthChance = 0.05f;

    /// <summary>Bleeding severity each easing takes off a wound.</summary>
    private static readonly FixedPoint2 WarmthClot = FixedPoint2.New(0.5);

    private void InitializeAbilities()
    {
        SubscribeLocalEvent<WFHemophageComponent, WFHemophageDormantActionEvent>(OnDormantAction);
        SubscribeLocalEvent<WFHemophageComponent, WFHemophageDormantDoAfterEvent>(OnDormantDoAfter);
        SubscribeLocalEvent<WFHemophageComponent, WFHemophageRegenActionEvent>(OnRegenAction);
        SubscribeLocalEvent<WFHemophageComponent, WFHemophageClotActionEvent>(OnClotAction);
        SubscribeLocalEvent<WFHemophageComponent, WFHemophageMasterActionEvent>(OnMasterAction);
        SubscribeLocalEvent<WFHemophageComponent, WFSuppressTasteEvent>(OnSuppressTaste);
    }

    private void TickAbilities(Entity<WFHemophageComponent, BloodstreamComponent> ent, float seconds)
    {
        var (uid, comp, blood) = ent;
        var normal = blood.BloodMaxVolume;

        if (comp.Regenerating)
            TickRegen(ent, seconds);

        if (comp.MasterOfTheHouse)
        {
            var ceiling = normal * comp.StartRatio;
            if (TotalBlood(ent) < ceiling)
                AddBlood(ent, normal * comp.MasterRegenPerSecond * seconds, ceiling);
        }

        if (comp.SlaveUntil is not { } until)
            return;

        if (comp.SlaveHealPerSecond > 0f && TryComp<DamageableComponent>(uid, out var damageable))
        {
            var current = damageable.Damage.DamageDict.GetValueOrDefault(Asphyxiation);
            var heal = FixedPoint2.Min(current, FixedPoint2.New(comp.SlaveHealPerSecond * seconds));
            if (heal > FixedPoint2.Zero)
            {
                var spec = new DamageSpecifier();
                spec.DamageDict[Asphyxiation] = -heal;
                _damageable.TryChangeDamage(uid, spec, ignoreResistances: true, interruptsDoAfters: false);
                RemoveBlood(ent, normal * comp.HealCostRatio * heal.Float());
            }
        }

        if (_timing.CurTime < until)
            return;

        comp.SlaveUntil = null;
        comp.SlaveHealPerSecond = 0f;
        EnsureComp<BreathingImmunityComponent>(uid);
        _alerts.ClearAlert(uid, comp.SlaveAlert);
    }

    #region Dormant state

    private void OnDormantAction(Entity<WFHemophageComponent> ent, ref WFHemophageDormantActionEvent args)
    {
        // Left unhandled: the cooldown starts only once the state actually changes.
        if (args.Handled)
            return;

        var key = ent.Comp.Dormant ? "wf-hemophage-dormant-leaving" : "wf-hemophage-dormant-entering";
        _popup.PopupEntity(Loc.GetString(key), ent, ent);

        var doAfter = new DoAfterArgs(EntityManager, ent, DormantDelay, new WFHemophageDormantDoAfterEvent(), ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        };
        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnDormantDoAfter(Entity<WFHemophageComponent> ent, ref WFHemophageDormantDoAfterEvent args)
    {
        if (args.Cancelled)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-dormant-cancelled"), ent, ent);
            return;
        }

        if (args.Handled)
            return;

        args.Handled = true;
        SetDormant(ent, !ent.Comp.Dormant);
        _actions.StartUseDelay(ent.Comp.DormantAction);
    }

    /// <summary>Puts the tumor to sleep or wakes it, with everything that comes with it.</summary>
    public void SetDormant(Entity<WFHemophageComponent> ent, bool dormant)
    {
        var (uid, comp) = ent;
        if (comp.Dormant == dormant)
            return;

        if (dormant)
        {
            SetRegen(ent, false);
            if (comp.MasterOfTheHouse)
                SetMaster(ent, false);
        }

        comp.Dormant = dormant;
        Dirty(uid, comp);

        var drain = CompOrNull<WFHemophageDrainerComponent>(uid)?.DrainAction;
        foreach (var action in new[] { comp.RegenAction, comp.ClotAction, comp.MasterAction, drain })
            _actions.SetEnabled(action, !dormant);

        _actions.SetToggled(comp.DormantAction, dormant);
        if (comp.DormantAction is { } dormantAction)
        {
            var name = dormant ? Loc.GetString("wf-hemophage-action-dormant-exit") : Loc.GetString("wf-hemophage-action-dormant-enter");
            var desc = dormant ? Loc.GetString("wf-hemophage-action-dormant-exit-desc") : Loc.GetString("wf-hemophage-action-dormant-enter-desc");
            _metaData.SetEntityName(dormantAction, name);
            _metaData.SetEntityDescription(dormantAction, desc);
        }

        if (TryComp<BodyComponent>(uid, out var body))
        {
            foreach (var tumor in _body.GetBodyOrganEntityComps<WFHemophageTumorComponent>((uid, body)))
                _appearance.SetData(tumor.Owner, WFHemophageTumorVisuals.Dormant, dormant);
        }

        ApplyStamina(ent);
        Movement.RefreshMovementSpeedModifiers(uid);

        var message = dormant ? "wf-hemophage-dormant-start" : "wf-hemophage-dormant-end";
        if (TryComp<Robust.Shared.Player.ActorComponent>(uid, out var actor))
            _chat.DispatchServerMessage(actor.PlayerSession, Loc.GetString(message));
    }

    #endregion

    #region Stamina

    /// <summary>Halves the stamina crit threshold under Master of the House.</summary>
    private void ApplyStamina(Entity<WFHemophageComponent> ent)
    {
        SetStaminaFactor(ent, ent.Comp.MasterOfTheHouse ? ent.Comp.MasterStaminaMultiplier : 1f);
    }

    private void RestoreStamina(Entity<WFHemophageComponent> ent)
    {
        SetStaminaFactor(ent, 1f);
    }

    /// <summary>Rescales the crit threshold by the change in factor, keeping changes other systems made meanwhile.</summary>
    private void SetStaminaFactor(Entity<WFHemophageComponent> ent, float factor)
    {
        var comp = ent.Comp;
        if (comp.AppliedStaminaFactor == factor || !TryComp<StaminaComponent>(ent, out var stamina))
            return;

        stamina.CritThreshold = stamina.CritThreshold / comp.AppliedStaminaFactor * factor;
        comp.AppliedStaminaFactor = factor;
        Dirty(ent, stamina);
    }

    #endregion

    #region Hemokinetic regeneration

    private void OnRegenAction(Entity<WFHemophageComponent> ent, ref WFHemophageRegenActionEvent args)
    {
        if (args.Handled || !TryComp<BloodstreamComponent>(ent, out var blood))
            return;

        args.Handled = true;
        if (ent.Comp.Regenerating)
        {
            SetRegen(ent, false);
            _popup.PopupEntity(Loc.GetString("wf-hemophage-regen-off"), ent, ent);
            return;
        }

        if (TotalBlood((ent, ent.Comp, blood)) <= blood.BloodMaxVolume * ent.Comp.PowerMinRatio)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-blood-too-low"), ent, ent, PopupType.SmallCaution);
            return;
        }

        if (BruteBurn(ent) >= ent.Comp.RegenDamageLimit)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-regen-too-damaged"), ent, ent, PopupType.SmallCaution);
            return;
        }

        SetRegen(ent, true);
        _popup.PopupEntity(Loc.GetString("wf-hemophage-regen-on"), ent, ent);
    }

    private void SetRegen(Entity<WFHemophageComponent> ent, bool on)
    {
        if (ent.Comp.Regenerating == on)
            return;

        ent.Comp.Regenerating = on;
        _actions.SetToggled(ent.Comp.RegenAction, on);
        if (on)
            _alerts.ShowAlert(ent, ent.Comp.RegenAlert);
        else
            _alerts.ClearAlert(ent, ent.Comp.RegenAlert);
    }

    private float BruteBurn(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return 0f;

        return damageable.DamagePerGroup.GetValueOrDefault(Brute).Float()
               + damageable.DamagePerGroup.GetValueOrDefault(Burn).Float();
    }

    /// <summary>
    /// One tick of regeneration, as Avali stasis heals: stored damage thinned and wounds closed at topical strength from
    /// the same budget, so the wounds a hit left close with its damage instead of outlasting it.
    /// </summary>
    private void TickRegen(Entity<WFHemophageComponent, BloodstreamComponent> ent, float seconds)
    {
        var (uid, comp, blood) = ent;
        var before = BruteBurn(uid);
        if (before >= comp.RegenDamageLimit)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-regen-too-damaged"), uid, uid, PopupType.SmallCaution);
            SetRegen((uid, comp), false);
            return;
        }

        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return;

        var amount = FixedPoint2.New(comp.RegenPerSecond * seconds);
        var heal = new DamageSpecifier();
        AddGroupHeal(heal, damageable, Brute, amount);
        AddGroupHeal(heal, damageable, Burn, amount);
        if (!heal.Empty && !_routing.TryApplyDamage(uid, heal, origin: uid, ignoreResistances: true, healWounds: false))
            _damageable.TryChangeDamage(uid, heal, ignoreResistances: true, interruptsDoAfters: false);

        var closed = CloseGroupWounds(uid, Brute, amount) + CloseGroupWounds(uid, Burn, amount);
        var healed = FixedPoint2.Max(FixedPoint2.New(before - BruteBurn(uid)), closed);
        if (healed > FixedPoint2.Zero)
            RemoveBlood(ent, blood.BloodMaxVolume * comp.HealCostRatio * healed.Float());
    }

    /// <summary>Spends <paramref name="budget"/> closing the body's wounds of the group's types.</summary>
    /// <returns>The budget spent.</returns>
    private FixedPoint2 CloseGroupWounds(EntityUid uid, ProtoId<DamageGroupPrototype> groupId, FixedPoint2 budget)
    {
        var left = budget;
        foreach (var type in _proto.Index(groupId).DamageTypes)
            left = _stasis.CloseWounds(uid, type, left);

        return budget - left;
    }

    /// <summary>
    /// Spreads <paramref name="amount"/> of healing over the group's types the body has actually taken, so a body hurt
    /// in one type still heals the full amount.
    /// </summary>
    private void AddGroupHeal(DamageSpecifier heal, DamageableComponent damageable, ProtoId<DamageGroupPrototype> groupId, FixedPoint2 amount)
    {
        var damaged = _proto.Index(groupId).DamageTypes
            .Where(type => damageable.Damage.DamageDict.GetValueOrDefault(type) > FixedPoint2.Zero)
            .ToList();
        if (damaged.Count == 0)
            return;

        var share = amount / damaged.Count;
        foreach (var type in damaged)
            heal.DamageDict[type] = -share;
    }

    #endregion

    #region Hemokinetic clot

    private void OnClotAction(Entity<WFHemophageComponent> ent, ref WFHemophageClotActionEvent args)
    {
        if (args.Handled || !TryComp<BloodstreamComponent>(ent, out var blood) || !TryComp<BodyComponent>(ent, out var body))
            return;

        var full = (ent.Owner, ent.Comp, blood);
        if (TotalBlood(full) <= blood.BloodMaxVolume * ent.Comp.PowerMinRatio)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-blood-too-low"), ent, ent, PopupType.SmallCaution);
            return;
        }

        EntityUid? worst = null;
        EntityUid worstPart = default;
        var worstRate = 0f;
        foreach (var part in _body.GetBodyChildren(ent, body))
        {
            foreach (var wound in _wounds.GetWounds(part.Id))
            {
                if (!TryComp<WoundBleedingComponent>(wound, out var bleeding) || bleeding.CurrentRate <= worstRate)
                    continue;

                worst = wound;
                worstPart = part.Id;
                worstRate = bleeding.CurrentRate;
            }
        }

        if (worst is not { } target || !TryComp<WoundBleedingComponent>(target, out var targetBleeding))
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-clot-nothing"), ent, ent, PopupType.SmallCaution);
            return;
        }

        args.Handled = true;
        ent.Comp.Clotted.Add(new WFClottedWound
        {
            Wound = target,
            Part = worstPart,
            BleedingSeverity = targetBleeding.BleedingSeverity,
            PartSeverity = PartSeverity(worstPart),
        });

        _bleeding.ReduceBleeding(target, targetBleeding.BleedingSeverity);
        RemoveBlood(full, blood.BloodMaxVolume * ent.Comp.ClotCostRatio);
        _popup.PopupEntity(Loc.GetString("wf-hemophage-clot-done", ("part", worstPart)), ent, ent);
    }

    private FixedPoint2 PartSeverity(EntityUid part)
    {
        var total = FixedPoint2.Zero;
        foreach (var wound in _wounds.GetWounds(part))
            total += wound.Comp.Severity;
        return total;
    }

    /// <summary>A clotted wound bursts open again as soon as its limb is hurt.</summary>
    private void TickClots(Entity<WFHemophageComponent, BloodstreamComponent> ent)
    {
        var (uid, comp, _) = ent;
        for (var i = comp.Clotted.Count - 1; i >= 0; i--)
        {
            var clot = comp.Clotted[i];
            if (TerminatingOrDeleted(clot.Wound) || TerminatingOrDeleted(clot.Part)
                || !TryComp<BodyPartComponent>(clot.Part, out var part) || part.Body != uid)
            {
                comp.Clotted.RemoveAt(i);
                continue;
            }

            var severity = PartSeverity(clot.Part);
            if (severity <= clot.PartSeverity)
            {
                clot.PartSeverity = severity;
                continue;
            }

            comp.Clotted.RemoveAt(i);
            var bleeding = EnsureComp<WoundBleedingComponent>(clot.Wound);
            bleeding.BleedingSeverity = FixedPoint2.Max(bleeding.BleedingSeverity, clot.BleedingSeverity);
            _bleeding.RefreshWound((clot.Wound, bleeding));
            _popup.PopupEntity(Loc.GetString("wf-hemophage-clot-reopened", ("part", clot.Part)), uid, uid, PopupType.MediumCaution);
        }
    }

    /// <summary>Blood worked through a corrupted liver now and then eases a bleeding wound.</summary>
    private void TickBloodWarmth(Entity<WFHemophageComponent, BloodstreamComponent> ent, float seconds)
    {
        var (uid, comp, _) = ent;
        if (_timing.CurTime >= comp.BloodWarmthUntil || !TryComp<BodyComponent>(uid, out var body) || !HasCorrupted<LiverComponent>((uid, body)))
            return;

        var helped = false;
        foreach (var part in _body.GetBodyChildren(uid, body))
        {
            // A copy: stopping a systemic bleed removes its wound from the container being walked.
            foreach (var wound in _wounds.GetWounds(part.Id).ToList())
            {
                if (!HasComp<WoundBleedingComponent>(wound) || !_random.Prob(WarmthChance * seconds))
                    continue;

                helped |= _bleeding.ReduceBleeding(wound.Owner, WarmthClot);
            }
        }

        if (helped && _timing.CurTime >= comp.NextWarmthMessage)
        {
            comp.NextWarmthMessage = _timing.CurTime + TimeSpan.FromMinutes(1);
            _popup.PopupEntity(Loc.GetString("wf-hemophage-blood-warmth"), uid, uid);
        }
    }

    /// <summary>Whether the body holds a corrupted organ of this kind.</summary>
    private bool HasCorrupted<T>(Entity<BodyComponent> body) where T : IComponent
    {
        foreach (var organ in _body.GetBodyOrganEntityComps<T>((body, body.Comp)))
        {
            if (TryComp<WFTumorCorruptedComponent>(organ.Owner, out var corrupted) && corrupted.Corrupted)
                return true;
        }

        return false;
    }

    #endregion

    #region Master of the house

    private void OnMasterAction(Entity<WFHemophageComponent> ent, ref WFHemophageMasterActionEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.SlaveUntil != null)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-master-recovering"), ent, ent, PopupType.SmallCaution);
            return;
        }

        args.Handled = true;
        SetMaster(ent, !ent.Comp.MasterOfTheHouse);
    }

    private void SetMaster(Entity<WFHemophageComponent> ent, bool on)
    {
        var (uid, comp) = ent;
        if (comp.MasterOfTheHouse == on)
            return;

        comp.MasterOfTheHouse = on;
        Dirty(uid, comp);
        _actions.SetToggled(comp.MasterAction, on);
        ApplyStamina(ent);
        Movement.RefreshMovementSpeedModifiers(uid);

        if (on)
        {
            RemoveBreathingImmunity((uid, comp));
            _alerts.ShowAlert(uid, comp.MasterAlert);
            _popup.PopupEntity(Loc.GetString("wf-hemophage-master-on"), uid, uid);
            return;
        }

        _alerts.ClearAlert(uid, comp.MasterAlert);
        var asphyxiation = CompOrNull<DamageableComponent>(uid)?.Damage.DamageDict.GetValueOrDefault(Asphyxiation) ?? FixedPoint2.Zero;
        if (asphyxiation <= FixedPoint2.Zero)
        {
            EnsureComp<BreathingImmunityComponent>(uid);
            _popup.PopupEntity(Loc.GetString("wf-hemophage-master-off"), uid, uid);
            return;
        }

        comp.SlaveUntil = _timing.CurTime + comp.SlaveDuration;
        comp.SlaveHealPerSecond = asphyxiation.Float() / (float) comp.SlaveDuration.TotalSeconds;
        _alerts.ShowAlert(uid, comp.SlaveAlert);
        _popup.PopupEntity(Loc.GetString("wf-hemophage-slave-start"), uid, uid);
    }

    #endregion

    private void OnSuppressTaste(Entity<WFHemophageComponent> ent, ref WFSuppressTasteEvent args)
    {
        var suppressed = EnsureComp<WFTasteSuppressedComponent>(ent);
        var until = _timing.CurTime + args.Duration;
        if (until > suppressed.Until)
            suppressed.Until = until;
    }
}
