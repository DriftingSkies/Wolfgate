using Content.Server.NPC.HTN;
using Content.Shared._WF.Fauna;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Hides <see cref="FaunaAmbushComponent"/> mobs until they attack and plays their whispers while they hunt.
/// </summary>
public sealed partial class FaunaAmbushSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const string VisionRadiusKey = "VisionRadius";
    private const string AggroVisionRadiusKey = "AggroVisionRadius";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaAmbushComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FaunaAmbushComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<FaunaAmbushComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<FaunaAmbushComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaAmbushComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var ambush, out var htn))
        {
            if (!_mobState.IsAlive(uid))
                continue;

            if (ambush.Revealed && ambush.Rehide != null && now >= ambush.HideAt)
            {
                Hide((uid, ambush));
                continue;
            }

            if (!htn.Blackboard.TryGetValue<EntityUid>(FaunaHelperSystem.TargetKey, out var target, EntityManager) ||
                !_mobState.IsAlive(target))
            {
                if (ambush.Revealed && now >= ambush.WakeUntil)
                    Hide((uid, ambush));

                continue;
            }

            if (ambush.Revealed || ambush.Whisper == null || now < ambush.NextWhisper)
                continue;

            ambush.NextWhisper = now + ambush.WhisperCooldown;
            if (_transform.InRange(uid, target, 1.5f))
                continue;

            _audio.PlayPvs(ambush.Whisper, uid);
        }
    }

    private void OnMapInit(Entity<FaunaAmbushComponent> ent, ref MapInitEvent args)
    {
        Hide(ent);
    }

    private void OnMeleeHit(Entity<FaunaAmbushComponent> ent, ref MeleeHitEvent args)
    {
        if (args.User != ent.Owner)
            return;

        if (ent.Comp.Revealed)
        {
            Reveal(ent);
            return;
        }

        if (args.IsHit && args.HitEntities.Count > 0)
        {
            if (ent.Comp.AmbushDamage is { } bonus)
                args.BonusDamage += bonus;

            if (ent.Comp.AmbushKnockdown is { } knockdown)
            {
                foreach (var target in args.HitEntities)
                {
                    _stun.TryKnockdown(target, knockdown, true);
                }
            }

            _audio.PlayPvs(ent.Comp.AmbushSound, ent);
        }

        Reveal(ent);
    }

    private void OnDamageChanged(Entity<FaunaAmbushComponent> ent, ref DamageChangedEvent args)
    {
        if (!ent.Comp.RevealOnDamage || !args.DamageIncreased)
            return;

        Reveal(ent);
        if (ent.Comp.WakeVisionRadius is not { } wake || !TryComp<HTNComponent>(ent, out var htn))
            return;

        ent.Comp.WakeUntil = _timing.CurTime + ent.Comp.WakeHold;
        if (ent.Comp.NormalVisionRadius == null &&
            htn.Blackboard.TryGetValue<float>(VisionRadiusKey, out var normal, EntityManager))
        {
            ent.Comp.NormalVisionRadius = normal;
        }

        htn.Blackboard.SetValue(VisionRadiusKey, wake);
        if (!htn.Blackboard.TryGetValue<float>(AggroVisionRadiusKey, out var aggro, EntityManager) || aggro < wake)
            htn.Blackboard.SetValue(AggroVisionRadiusKey, wake);
    }

    private void OnMobStateChanged(Entity<FaunaAmbushComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Reveal(ent);
    }

    private void Reveal(Entity<FaunaAmbushComponent> ent)
    {
        if (ent.Comp.Rehide is { } rehide)
            ent.Comp.HideAt = _timing.CurTime + rehide;

        if (ent.Comp.Revealed)
            return;

        ent.Comp.Revealed = true;
        if (HasComp<StealthComponent>(ent))
            _stealth.SetVisibility(ent, 1f);

        _appearance.SetData(ent, FaunaAmbushVisuals.Revealed, true);
    }

    private void Hide(Entity<FaunaAmbushComponent> ent)
    {
        ent.Comp.Revealed = false;
        if (ent.Comp.NormalVisionRadius is { } normal && TryComp<HTNComponent>(ent, out var htn))
        {
            htn.Blackboard.SetValue(VisionRadiusKey, normal);
            ent.Comp.NormalVisionRadius = null;
        }

        if (TryComp<StealthComponent>(ent, out var stealth))
            _stealth.SetVisibility(ent, ent.Comp.HiddenVisibility, stealth);

        _appearance.SetData(ent, FaunaAmbushVisuals.Revealed, false);
    }
}
