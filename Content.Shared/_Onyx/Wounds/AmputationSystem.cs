using System.Numerics;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Shared.Throwing;
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE(Wolfmed): D8, Onyx's amputation part fields live on WolfmedBodyPartComponent.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): D12 facade + the TryDetachPart shim.
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE(Wolfmed): V124, the dismemberment feedback event.

namespace Content.Shared._Onyx.Wounds;

public sealed partial class AmputationSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedDamageableSystem _damageable = default!; // WOLFGATE(Wolfmed): D12, GetAllDamage lives on the compat facade.
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private WolfmedBodyPartSystem _wfPart = default!; // WOLFGATE(Wolfmed): D8
    [Dependency] private WolfmedBodySystem _wfBody = default!; // WOLFGATE(Wolfmed): Onyx's SharedBodySystem.TryDetachPart

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundableComponent, PartDamageOverflowedEvent>(OnPartDamageOverflowed);
    }

    private void OnPartDamageOverflowed(Entity<WoundableComponent> part, ref PartDamageOverflowedEvent args)
    {
        if (!_net.IsServer ||
            !TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body == null ||
            _wfPart.Get(part).MaxDamage <= FixedPoint2.Zero) // WOLFGATE(Wolfmed): D8
            return;

        // WOLFGATE(Wolfmed): revival pass, a capped head never comes off from piled-up damage
        // Its overflow is brain injury. Only a blast may still take it, where wolfmed.blast_dismember_head allows.
        if (bodyPart.PartType == BodyPartType.Head && !(args.IsExplosion && _wfPart.BlastMaySever(part)))
            return;

        // WOLFGATE(Wolfmed): EVISC: D9 never severs a torso, so its overflow is offered to evisceration instead.
        if (bodyPart.PartType == BodyPartType.Torso)
        {
            var torso = new WolfmedTorsoOverflowEvent(args.Body, part.Owner, args.Damage, args.IsExplosion);
            RaiseLocalEvent(ref torso);
            return;
        }

        _wfPart.AddOverflow(part, args.Damage); // WOLFGATE(Wolfmed): revival pass, the sever thresholds count it

        // WOLFGATE(Wolfmed): revival pass, the blast roll reads the part's whole total as the threshold path does
        // That is what it stored, what overflowed (this hit's included) and the share of this hit that fills it to its
        // cap, which lands only after this event.
        if (args.ExplosionAmputationCandidate && TryExplosionAmputate(args.Body, part, args.Damage, // WOLFGATE(Wolfmed): P3-D15
                ExplosionTotal(part, args.Damage)))
            return;

        if (!part.Comp.Severable)
        {
            // WOLFGATE(Wolfmed) START: revival pass, a limb is armed by its thresholds with the overflow counted
            // (HandlePartDamageApplied); one with none, like a plant limb, is never armed by overflow.
            // if (part.Comp.AmputationOverflow >= _wfPart.Get(part).MaxDamage) // WOLFGATE(Wolfmed): D8
            // {
            //     SetSeverable(part, true);
            // }
            // WOLFGATE END
            return;
        }

        if (IsFinishingHit(args.Body, part.Owner, args.Damage)) // WOLFGATE(Wolfmed): P3-D15
            TryAmputate(args.Body, part.Owner);
    }

    public void ApplyAmputationConsequences(EntityUid body, EntityUid parent)
    {
        if (!_net.IsServer || !HasComp<WoundableComponent>(parent) ||
            !TryComp(parent, out BodyPartComponent? parentPart))
            return;

        if (!TryComp(body, out WoundHostComponent? host))
            return;

        // WOLFGATE(Wolfmed): D8. The severity comes off the PARENT the stump is left on, not off the severed part.
        _wounds.CreateOrMergeWound(parent, host.AmputationConsequenceWound, _wfPart.Get(parent).AmputationConsequenceSeverity);
    }

    public void HandlePartDamageApplied(Entity<WoundableComponent> part, ref PartDamageAppliedEvent args)
    {
        if (!_net.IsServer)
            return;

        if (!TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body == null ||
            bodyPart.PartType is BodyPartType.Torso || // WOLFGATE(Wolfmed): D9
            _body.GetParentPartOrNull(part) is null || // WOLFGATE(Wolfmed): Shitmed's BodyPartComponent has no Parent field.
            _wfPart.Get(part).AmputationThresholds.Count == 0 || // WOLFGATE(Wolfmed): D8
            !TryComp(part, out DamageableComponent? damageable))
            return;

        // WOLFGATE(Wolfmed): M3: P27, a blast never takes the head unless wolfmed.blast_dismember_head is on.
        if (args.IsExplosion && !_wfPart.BlastMaySever(part))
            return;

        // WOLFGATE(Wolfmed) START: revival pass, healing a capped part back under its cap clears its overflow
        // Only a damage hit cleared it in the routing.
        if (args.Damage.GetTotal() < FixedPoint2.Zero &&
            _wfPart.Get(part).MaxDamage is var healCap && healCap > FixedPoint2.Zero &&
            _damageable.GetPositiveDamage((part.Owner, damageable)).GetTotal() < healCap)
        {
            _wfPart.ClearOverflow(part);
            if (part.Comp.AmputationOverflow != FixedPoint2.Zero)
            {
                part.Comp.AmputationOverflow = FixedPoint2.Zero;
                Dirty(part);
            }
        }
        // WOLFGATE END

        var damage = _wfPart.WithOverflow(part, _damageable.GetAllDamage((part.Owner, damageable))); // WOLFGATE(Wolfmed): revival pass, a capped part's overflow counts
        if (args.ExplosionAmputationCandidate && TryExplosionAmputate(args.Body, part, args.Damage, damage)) // WOLFGATE(Wolfmed): P3-D15
            return;

        if (!part.Comp.Severable)
        {
            if (ReachedThreshold(damage, _wfPart.Get(part).AmputationThresholds)) // WOLFGATE(Wolfmed): D8
            {
                SetSeverable(part, true);
            }
            return;
        }

        if (GetThresholdProgress(damage, _wfPart.Get(part).AmputationThresholds) < GetResetRatio(args.Body) && // WOLFGATE(Wolfmed): D8
            !OverflowHoldsSeverable(part)) // WOLFGATE(Wolfmed): revival pass, a capped part's overflow keeps it severable
        {
            SetSeverable(part, false);
            return;
        }

        var damageBeforeHit = damage.Clone();
        foreach (var (type, amount) in args.Damage.DamageDict)
            if (amount > FixedPoint2.Zero)
                damageBeforeHit.DamageDict[type] = FixedPoint2.Max(FixedPoint2.Zero,
                    damageBeforeHit.DamageDict.GetValueOrDefault(type) - amount);

        if (ReachedThreshold(damageBeforeHit, _wfPart.Get(part).AmputationThresholds) && // WOLFGATE(Wolfmed): D8
            IsFinishingHit(args.Body, part.Owner, args.Damage)) // WOLFGATE(Wolfmed): P3-D15
            TryAmputate(args.Body, part.Owner);
    }

    /// <summary>
    /// Detaches the part deterministically and applies amputation consequences.
    /// </summary>
    public bool TryAmputate(EntityUid body, EntityUid part)
    {
        if (!_net.IsServer || !TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body == null ||
            bodyPart.PartType == BodyPartType.Torso) // WOLFGATE(Wolfmed): D9
            return false;

        var parent = _body.GetParentPartOrNull(part) ?? part; // WOLFGATE(Wolfmed): Shitmed has no BodyPartComponent.Parent.
        if (!_wfBody.TryDetachPart(part)) // WOLFGATE(Wolfmed): compat shim
            return false;

        if (TryComp(body, out WoundHostComponent? host))
            _wounds.CreateOrMergeWound(parent, host.DismembermentWound,
                _wfPart.Get(part).DismembermentSeverity ?? GetDismembermentSeverity(host, bodyPart.PartType)); // WOLFGATE(Wolfmed): D8
        ApplyAmputationConsequences(body, parent);
        _throwing.TryThrow(part, Vector2.UnitY, baseThrowSpeed: 3f,
            pushbackRatio: 0f, doSpin: true);
        // WOLFGATE(Wolfmed): V124: the one damage-driven detach path, so the dismemberment feedback hangs here
        // rather than on TryDetachPart, which surgery also uses.
        var amputated = new WolfmedPartAmputatedEvent(body, part, parent);
        RaiseLocalEvent(ref amputated);
        return true;
    }

    private static bool ReachedThreshold(
        DamageSpecifier damage,
        IReadOnlyDictionary<ProtoId<DamageTypePrototype>, FixedPoint2> thresholds)
    {
        return GetThresholdProgress(damage, thresholds) >= 1f;
    }

    private static float GetThresholdProgress(
        DamageSpecifier damage,
        IReadOnlyDictionary<ProtoId<DamageTypePrototype>, FixedPoint2> thresholds)
    {
        var progress = 0f;
        foreach (var (type, threshold) in thresholds)
        {
            if (threshold <= FixedPoint2.Zero)
                continue;
            progress += (float) damage.DamageDict.GetValueOrDefault(type) / (float) threshold;
        }
        return progress;
    }

    private bool IsFinishingHit(EntityUid body, EntityUid part, DamageSpecifier damage) // WOLFGATE(Wolfmed): D8/P3-D15
    {
        var wf = _wfPart.Get(part); // WOLFGATE(Wolfmed): D8
        if (!TryComp(body, out WoundHostComponent? host))
            return false;

        foreach (var (type, amount) in damage.DamageDict)
        {
            if (amount <= FixedPoint2.Zero || !wf.AmputationThresholds.ContainsKey(type))
                continue;

            var minimum = wf.DismembermentFinishingDamage.GetValueOrDefault(type,
                host.DefaultDismembermentFinishingDamage.GetValueOrDefault(type));
            if (minimum > FixedPoint2.Zero && amount >= minimum)
                // WOLFGATE(Wolfmed) START: worn armour with a limb lock may keep the limb on.
                // return true;
                return WfMaySever(body, part, amount);
                // WOLFGATE END
        }

        return false;
    }

    // WOLFGATE(Wolfmed) START: asked only once a hit would take the limb, so ordinary hits skip the inventory relay.
    private bool WfMaySever(EntityUid body, EntityUid part, FixedPoint2 finishing)
    {
        var ev = new Content.Shared._WF.Wolfmed.Wounds.WolfmedSeverAttemptEvent(part, finishing);
        RaiseLocalEvent(body, ref ev);
        return !ev.Cancelled;
    }
    // WOLFGATE END

    private bool TryExplosionAmputate(
        EntityUid body,
        Entity<WoundableComponent> part,
        // WOLFGATE(Wolfmed): P3-D15 drops Onyx's BodyPartComponent parameter; the data it read is on WolfmedBodyPartComponent.
        DamageSpecifier hit,
        DamageSpecifier? totalDamage = null)
    {
        if (!IsFinishingHit(body, part.Owner, hit)) // WOLFGATE(Wolfmed): P3-D15
            return false;

        if (totalDamage == null)
        {
            totalDamage = _damageable.GetAllDamage(part.Owner).Clone();
            foreach (var (type, amount) in hit.DamageDict)
                if (amount > FixedPoint2.Zero)
                    totalDamage.DamageDict[type] = totalDamage.DamageDict.GetValueOrDefault(type) + amount;
        }

        var chance = Math.Clamp(GetThresholdProgress(totalDamage, _wfPart.Get(part.Owner).AmputationThresholds) * 0.5f, 0f, 1f); // WOLFGATE(Wolfmed): D8
        return chance > 0f && _random.Prob(chance) && TryAmputate(body, part.Owner);
    }

    // WOLFGATE(Wolfmed) START: revival pass, an overflowing hit's whole effect on a capped part, for the blast roll.
    private DamageSpecifier ExplosionTotal(Entity<WoundableComponent> part, DamageSpecifier overflow)
    {
        var stored = _damageable.GetAllDamage(part.Owner).Clone();
        var room = TryComp(part, out DamageableComponent? partDamage)
            ? _wfPart.Get(part).MaxDamage - _damageable.GetPositiveDamage((part.Owner, partDamage)).GetTotal()
            : FixedPoint2.Zero;
        var over = overflow.GetTotal();
        if (room > FixedPoint2.Zero && over > FixedPoint2.Zero)
        {
            foreach (var (type, amount) in overflow.DamageDict)
            {
                if (amount > FixedPoint2.Zero)
                    stored.DamageDict[type] = stored.DamageDict.GetValueOrDefault(type) + amount * (room.Float() / over.Float());
            }
        }

        return _wfPart.WithOverflow(part, stored);
    }
    // WOLFGATE END

    // WOLFGATE(Wolfmed) START: revival pass, a capped part never reaches its stored thresholds, so its overflow
    // decides; the wound ladders' top rung marks a limb the same way.
    private bool OverflowHoldsSeverable(Entity<WoundableComponent> part)
    {
        var cap = _wfPart.Get(part).MaxDamage;
        return cap > FixedPoint2.Zero && part.Comp.AmputationOverflow >= cap;
    }

    /// <summary>
    /// Marks a limb severable: the next finishing hit takes it. Its overflow is raised to its cap so the stored
    /// thresholds do not clear the mark again. Never the torso or the head.
    /// </summary>
    public void MarkSeverable(Entity<WoundableComponent> part)
    {
        if (!TryComp(part, out BodyPartComponent? bodyPart) ||
            bodyPart.PartType is BodyPartType.Torso or BodyPartType.Head)
            return;

        var cap = _wfPart.Get(part).MaxDamage;
        if (cap > FixedPoint2.Zero && part.Comp.AmputationOverflow < cap)
        {
            part.Comp.AmputationOverflow = cap;
            Dirty(part);
        }

        SetSeverable(part, true);
    }
    // WOLFGATE END

    private float GetResetRatio(EntityUid body)
    {
        return TryComp(body, out WoundHostComponent? host)
            ? Math.Clamp(host.SeverableResetRatio, 0f, 1f)
            : 0.8f;
    }

    private void SetSeverable(Entity<WoundableComponent> part, bool value)
    {
        if (part.Comp.Severable == value)
            return;

        part.Comp.Severable = value;
        Dirty(part);
    }

    private static FixedPoint2 GetDismembermentSeverity(WoundHostComponent host, BodyPartType type)
    {
        return host.DismembermentSeverities.GetValueOrDefault(type, host.DefaultDismembermentSeverity);
    }
}
