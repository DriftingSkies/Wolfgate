using Content.Goobstation.Shared.StationRadio.Components;
using Content.Goobstation.Shared.StationRadio.Events;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Audio.Systems;

namespace Content.Goobstation.Shared.StationRadio.Systems;

public sealed class StationRadioReceiverSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationRadioReceiverComponent, StationRadioMediaPlayedEvent>(OnMediaPlayed);
        SubscribeLocalEvent<StationRadioReceiverComponent, StationRadioMediaStoppedEvent>(OnMediaStopped);
        SubscribeLocalEvent<StationRadioReceiverComponent, ActivateInWorldEvent>(OnRadioToggle);
        SubscribeLocalEvent<StationRadioReceiverComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnPowerChanged(EntityUid uid, StationRadioReceiverComponent comp, PowerChangedEvent args)
    {
        // WOLFGATE START: the track's audio entity despawns when it ends, and a volume is not a gain
        // if(comp.SoundEntity != null && args.Powered)
        //     _audio.SetGain(comp.SoundEntity, comp.Active ? comp.DefaultParams.Volume : 0f);
        // else if(comp.SoundEntity != null)
        //     _audio.SetGain(comp.SoundEntity, 0);
        WfSetAudible(comp, args.Powered && comp.Active);
        // WOLFGATE END
    }

    // WOLFGATE START: one place that mutes or restores the playing track
    private void WfSetAudible(StationRadioReceiverComponent comp, bool audible)
    {
        if (comp.SoundEntity is not { } sound)
            return;

        if (!Exists(sound))
        {
            comp.SoundEntity = null;
            return;
        }

        if (audible)
            _audio.SetVolume(sound, comp.DefaultParams.Volume);
        else
            _audio.SetGain(sound, 0f);
    }
    // WOLFGATE END

    private void OnRadioToggle(EntityUid uid, StationRadioReceiverComponent comp, ActivateInWorldEvent args)
    {
        comp.Active = !comp.Active;
        // WOLFGATE START: see WfSetAudible
        // if (comp.SoundEntity != null && _power.IsPowered(uid))
        //     _audio.SetGain(comp.SoundEntity, comp.Active ? comp.DefaultParams.Volume : 0f);
        if (_power.IsPowered(uid))
            WfSetAudible(comp, comp.Active);
        // WOLFGATE END
    }

    private void OnMediaPlayed(EntityUid uid, StationRadioReceiverComponent comp, StationRadioMediaPlayedEvent args)
    {
        var audio = _audio.PlayPredicted(args.MediaPlayed, uid, uid, comp.DefaultParams);
        if (audio != null && _power.IsPowered(uid) && comp.Active)
            comp.SoundEntity = audio.Value.Entity;
        else if (audio != null && !_power.IsPowered(uid) || !comp.Active && audio != null)
        {
            comp.SoundEntity = audio.Value.Entity;
            _audio.SetGain(comp.SoundEntity, 0);
        }
    }

    private void OnMediaStopped(EntityUid uid, StationRadioReceiverComponent comp, StationRadioMediaStoppedEvent args)
    {
        if (comp.SoundEntity == null)
            return;

        comp.SoundEntity = _audio.Stop(comp.SoundEntity);
    }

    // WOLFGATE START: forget a finished track's audio entity, whose uid would otherwise still be networked
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<StationRadioReceiverComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.SoundEntity is { } sound && TerminatingOrDeleted(sound))
                comp.SoundEntity = null;
        }
    }
    // WOLFGATE END
}
