using Content.Server._WF.Outposts.Components;
using Content.Shared.Audio;
using Content.Server.Lathe.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WF.Outposts;

/// <summary>Plays a lathe's working loop from the first print of its queue until the queue ends or it loses power.</summary>
public sealed class WFLatheRunningSoundSystem : EntitySystem
{
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<LatheProducingComponent, ComponentStartup>(OnStartProducing);
        SubscribeLocalEvent<LatheProducingComponent, ComponentShutdown>(OnStopProducing);
    }

    private void OnStartProducing(Entity<LatheProducingComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<WFLatheRunningSoundComponent>(ent, out var sound))
            return;

        _audio.PlayPvs(sound.StartSound, ent);
        _ambient.SetAmbience(ent, true);
    }

    private void OnStopProducing(Entity<LatheProducingComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<WFLatheRunningSoundComponent>(ent, out var sound) || TerminatingOrDeleted(ent))
            return;

        _audio.PlayPvs(sound.EndSound, ent);
        _ambient.SetAmbience(ent, false);
    }
}
