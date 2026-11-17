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
