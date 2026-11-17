using Content.Client.Audio;
using Content.Shared._Mono.CCVar;
using Content.Shared._WG.CombatMusic;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.CombatMusic;

/// <summary>Plays one combat track from the character editor at the combat music volume, pausing other music meanwhile.</summary>
public sealed partial class WGCombatMusicPreviewSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ContentAudioSystem _music = default!;

    private EntityUid? _stream;

    /// <summary>The track being previewed, or null.</summary>
    public ProtoId<WGCombatTrackPrototype>? Playing { get; private set; }

    /// <summary>Raised when a preview starts, stops or ends on its own.</summary>
    public event Action<ProtoId<WGCombatTrackPrototype>?>? PreviewChanged;

    public override void Shutdown()
    {
        base.Shutdown();
        Stop();
    }

    public void Play(WGCombatTrackPrototype track)
    {
        StopStream();
        var volume = SharedAudioSystem.GainToVolume(_cfg.GetCVar(MonoCVars.CombatMusicVolume));
        _stream = _audio.PlayGlobal(new ResolvedPathSpecifier(track.Path), Filter.Local(), false, AudioParams.Default.WithVolume(volume))?.Entity;
        Playing = track.ID;
        _music.PauseMusicWolfgate(WgMusicPauseReason.CombatMusicPreview);
        PreviewChanged?.Invoke(Playing);
    }

    public void Stop()
    {
        if (Playing == null && _stream == null)
            return;

        StopStream();
        Playing = null;
        _music.ResumeMusicWolfgate(WgMusicPauseReason.CombatMusicPreview);
        PreviewChanged?.Invoke(null);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (Playing == null)
            return;

        // The track ran out on its own.
        if (_stream is not { } stream || Deleted(stream))
        {
            Stop();
            return;
        }

        // Music that starts mid-preview, such as the next lobby song, gets paused too.
        _music.EnforceMusicPauseWolfgate();
    }

    private void StopStream()
    {
        if (_stream is { } stream && !Deleted(stream))
            QueueDel(stream);

        _stream = null;
    }
}
