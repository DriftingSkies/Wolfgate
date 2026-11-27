using Content.Shared._WG.Performance;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._WG.Performance;

/// <summary>Plays the light sounds the server sends, and predicts the local player's own.</summary>
public sealed class WGLightSoundSystem : SharedWGLightSoundSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;

    /// <summary>Sounds played from server messages; for tests.</summary>
    public int Played { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGLightSoundEvent>(OnSound);
    }

    private void OnSound(WGLightSoundEvent ev)
    {
        // A source outside this client's view has no sound to hear.
        if (!TryGetEntity(ev.Source, out var source) || TerminatingOrDeleted(source))
            return;

        _audio.PlayEntity(ev.Sound, Filter.Local(), source.Value, false, ev.Params);
        Played++;
    }

    public override void PlayPredicted(SoundSpecifier? sound, EntityUid source, EntityUid? user, AudioParams? audioParams = null)
    {
        _audio.PlayPredicted(sound, source, user, audioParams);
    }

    public override void PlayPvs(SoundSpecifier? sound, EntityUid source, AudioParams? audioParams = null)
    {
        _audio.PlayPvs(sound, source, audioParams);
    }
}
