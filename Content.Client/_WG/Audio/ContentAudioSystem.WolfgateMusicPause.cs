using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;

namespace Content.Client.Audio;

/// <summary>What is holding the music paused; it resumes once none are left.</summary>
[Flags]
public enum WgMusicPauseReason : byte
{
    None = 0,
    InternetSound = 1 << 0,
    SoundtrackMute = 1 << 1,
    CombatMusicPreview = 1 << 2,
    CodaSong = 1 << 3,
}

/// <summary>
/// Lets internet sounds, the soundtrack mute and combat music previews pause ambient, combat and lobby music, then resume it.
/// A partial so the music systems stay untouched.
/// </summary>
public sealed partial class ContentAudioSystem
{
    private readonly HashSet<EntityUid> _wgPausedMusic = new();
    private WgMusicPauseReason _wgMusicPauseReasons;

    /// <summary>
    /// Pauses current music and keeps any new track paused until every reason is resumed.
    /// </summary>
    public void PauseMusicWolfgate(WgMusicPauseReason reason)
    {
        _wgMusicPauseReasons |= reason;
        EnforceMusicPauseWolfgate();
    }

    /// <summary>Whether something other than the Coda's song is holding music paused, such as an internet sound or the
    /// soundtrack mute; the song falls silent with it.</summary>
    public bool WgMusicHeldElsewhere => (_wgMusicPauseReasons & ~WgMusicPauseReason.CodaSong) != 0;

    /// <summary>Whether combat music is playing now; the Coda's song blends with it rather than holding it off.</summary>
    public bool WgCombatMusicPlaying => _isCombatMusicPlaying && _currentlyPlaying == MusicType.Combat;

    /// <summary>The ordinary ambient track playing now, if any: not combat music, not a Lattice band's own piece.</summary>
    public EntityUid? WgAmbientTrack => _ambientMusicStream is { } stream && !TerminatingOrDeleted(stream)
        && _currentlyPlaying != MusicType.Combat && stream != _wgBandStream && _musicProto?.ID != "WGCodaSilence" ? stream : null;

    /// <summary>Fades the ambient track playing now out over a few seconds and stops it, for a seamless handover.</summary>
    public void FadeOutAmbientWolfgate(float seconds)
    {
        if (WgAmbientTrack is { } stream && !_fadingOut.ContainsKey(stream))
            FadeOut(stream, duration: seconds);
    }

    /// <summary>Fades out whatever music is playing now, combat included, and stops it.</summary>
    public void FadeOutMusicWolfgate(float seconds)
    {
        if (_ambientMusicStream is { } stream && !TerminatingOrDeleted(stream) && !_fadingOut.ContainsKey(stream))
            FadeOut(stream, duration: seconds);
    }

    /// <summary>Drops a pause reason like <see cref="ResumeMusicWolfgate"/>, fading the music back in rather than
    /// starting it at full volume.</summary>
    public void ResumeMusicFadingWolfgate(WgMusicPauseReason reason, float seconds)
    {
        var resuming = new List<EntityUid>(_wgPausedMusic);
        ResumeMusicWolfgate(reason);
        if (_wgMusicPauseReasons != WgMusicPauseReason.None)
            return;

        foreach (var uid in resuming)
        {
            if (TryComp<AudioComponent>(uid, out var audio) && audio.State == AudioState.Playing && !_fadingIn.ContainsKey(uid))
                FadeIn(uid, audio, seconds);
        }
    }

    /// <summary>
    /// Pauses tracks the music loops started since the last call. Run every frame while paused.
    /// </summary>
    public void EnforceMusicPauseWolfgate()
    {
        if (_wgMusicPauseReasons == WgMusicPauseReason.None)
            return;

        PauseMusicStream(_ambientMusicStream);
        PauseMusicStream(_lobbySoundtrackInfo?.MusicStreamEntityUid);
    }

    /// <summary>
    /// Drops a pause reason and, once none are left, resumes every track that still exists.
    /// </summary>
    public void ResumeMusicWolfgate(WgMusicPauseReason reason)
    {
        _wgMusicPauseReasons &= ~reason;
        if (_wgMusicPauseReasons != WgMusicPauseReason.None)
            return;

        foreach (var uid in _wgPausedMusic)
        {
            if (TryComp<AudioComponent>(uid, out var audio) && audio.State == AudioState.Paused)
                _audio.SetState(uid, AudioState.Playing, component: audio);
        }

        _wgPausedMusic.Clear();
    }

    private void PauseMusicStream(EntityUid? uid)
    {
        if (uid == null || !TryComp<AudioComponent>(uid, out var audio))
            return;

        // The engine starts a new track on its first audio frame even if we paused it before then,
        // leaving it marked paused but audible. Combat and biome changes hit this, so re-pause those too.
        var restarted = audio.State == AudioState.Paused && audio.Playing;
        if (audio.State != AudioState.Playing && !restarted)
            return;

        _audio.SetState(uid, AudioState.Paused, force: restarted, component: audio);
        _wgPausedMusic.Add(uid.Value);
    }
}
