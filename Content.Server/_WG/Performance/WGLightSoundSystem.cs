using Content.Shared._WG.Performance;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._WG.Performance;

/// <summary>Sends light sounds to the players in range of their source.</summary>
public sealed class WGLightSoundSystem : SharedWGLightSoundSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    private bool _enabled;

    /// <summary>Sounds sent as messages this round; for tests and tuning.</summary>
    public int Sent { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, WGLightSoundCVars.Enabled, v => _enabled = v, true);
    }

    public override void PlayPredicted(SoundSpecifier? sound, EntityUid source, EntityUid? user, AudioParams? audioParams = null)
    {
        if (sound == null)
            return;

        if (_enabled)
            Send(sound, source, user, audioParams);
        else
            _audio.PlayPredicted(sound, source, user, audioParams);
    }

    public override void PlayPvs(SoundSpecifier? sound, EntityUid source, AudioParams? audioParams = null)
    {
        if (sound == null)
            return;

        if (_enabled)
            Send(sound, source, null, audioParams);
        else
            _audio.PlayPvs(sound, source, audioParams);
    }

    private void Send(SoundSpecifier sound, EntityUid source, EntityUid? user, AudioParams? audioParams)
    {
        if (TerminatingOrDeleted(source))
            return;

        var filter = Filter.Pvs(source, entityManager: EntityManager, playerManager: _player, cfgManager: _cfg);
        if (user is { } predicts)
            filter.RemovePlayerByAttachedEntity(predicts);

        // Nobody in range hears it: nothing is made or sent.
        if (filter.Count == 0)
            return;

        var ev = new WGLightSoundEvent
        {
            Source = GetNetEntity(source),
            Sound = _audio.ResolveSound(sound),
            Params = audioParams ?? sound.Params,
        };

        RaiseNetworkEvent(ev, filter);
        Sent++;
    }
}
