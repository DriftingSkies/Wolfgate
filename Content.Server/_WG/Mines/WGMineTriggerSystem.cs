using Content.Server.Explosion.EntitySystems;
using Content.Shared._WG.Mines;
using Content.Shared.Popups;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WG.Mines;

/// <summary>Warns whoever steps on an armed mine and sets it off when they step off.</summary>
public sealed partial class WGMineTriggerSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WGArmableMineComponent, StepTriggeredOnEvent>(OnStepOn);
        SubscribeLocalEvent<WGArmableMineComponent, StepTriggeredOffEvent>(OnStepOff);
    }

    private void OnStepOn(Entity<WGArmableMineComponent> ent, ref StepTriggeredOnEvent args)
    {
        if (ent.Comp.State != WGMineState.Armed)
            return;

        _popup.PopupCoordinates(Loc.GetString("wg-mine-stepped-on", ("mine", ent)),
            Transform(ent).Coordinates,
            args.Tripper,
            PopupType.LargeCaution);
        _audio.PlayPvs(ent.Comp.StepSound, ent);
    }

    // The step trigger still reports stepping off after a disarm, so only an armed mine goes off.
    private void OnStepOff(Entity<WGArmableMineComponent> ent, ref StepTriggeredOffEvent args)
    {
        if (ent.Comp.State == WGMineState.Armed)
            _trigger.Trigger(ent, args.Tripper);
    }
}
