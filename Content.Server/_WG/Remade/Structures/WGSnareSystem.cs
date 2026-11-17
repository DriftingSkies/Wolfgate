using Content.Server._WG.Remade.Conversion;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WG.Remade.Structures;

/// <summary>A snare trips whoever steps on it and slows them. It rides on step triggers, so walking carefully gets past it.</summary>
public sealed partial class WGSnareSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private WGGrowthSystem _growth = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSnareComponent, StepTriggerAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WGSnareComponent, StepTriggeredOnEvent>(OnTriggered);
    }

    private void OnAttempt(Entity<WGSnareComponent> ent, ref StepTriggerAttemptEvent args)
    {
        args.Continue = _growth.IsActive(ent)
                        && _timing.CurTime >= ent.Comp.ReadyAt
                        && HasComp<MobStateComponent>(args.Tripper)
                        && !HasComp<WGRemadeComponent>(args.Tripper)
                        && !_mobState.IsDead(args.Tripper);
    }

    private void OnTriggered(Entity<WGSnareComponent> ent, ref StepTriggeredOnEvent args)
    {
        var snare = ent.Comp;
        snare.ReadyAt = _timing.CurTime + snare.Reload;
        snare.Trips++;

        _stun.TryKnockdown(args.Tripper, snare.Knockdown, true);
        _stun.TrySlowdown(args.Tripper, snare.Slow, true, snare.SlowMultiplier, snare.SlowMultiplier);
        _audio.PlayPvs(snare.TripSound, ent);
        _popup.PopupEntity(Loc.GetString("wg-remade-snare-trip"), args.Tripper, args.Tripper, PopupType.MediumCaution);
    }
}
