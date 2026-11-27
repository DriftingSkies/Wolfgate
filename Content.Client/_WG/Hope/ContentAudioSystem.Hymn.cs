using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;

namespace Content.Client.Audio;

/// <summary>What a hymn needs from the music: to fade it out, hold it, and fade it back in. A partial so the music
/// systems stay untouched.</summary>
public sealed partial class ContentAudioSystem
{
    /// <summary>What silences a hymn as well as the music: an internet sound, the soundtrack mute or a combat preview.</summary>
    private const WgMusicPauseReason WgHymnSilencers =
        WgMusicPauseReason.InternetSound | WgMusicPauseReason.SoundtrackMute | WgMusicPauseReason.CombatMusicPreview;

    /// <summary>Whether combat music is playing now; a hymn gives way to it.</summary>
    public bool WgHymnCombatPlaying => _isCombatMusicPlaying && _currentlyPlaying == MusicType.Combat;

    /// <summary>Whether something that silences music, like an internet sound or the soundtrack mute, holds it now.</summary>
    public bool WgHymnSilenced => (_wgMusicPauseReasons & WgHymnSilencers) != 0;

    /// <summary>Fades out whatever music is playing now, combat included, and stops it.</summary>
    public void WgHymnFadeOutMusic(float seconds)
    {
        if (_ambientMusicStream is { } stream && !TerminatingOrDeleted(stream) && !_fadingOut.ContainsKey(stream))
            FadeOut(stream, duration: seconds);
    }

    /// <summary>Drops the hymn's hold on the music and, once nothing else holds it, fades it back in.</summary>
    public void WgHymnReleaseMusic(float seconds)
    {
        var resuming = new List<EntityUid>(_wgPausedMusic);
        ResumeMusicWolfgate(WgMusicPauseReason.Hymn);
        if (_wgMusicPauseReasons != WgMusicPauseReason.None)
            return;

        foreach (var uid in resuming)
        {
            if (TryComp<AudioComponent>(uid, out var audio) && audio.State == AudioState.Playing && !_fadingIn.ContainsKey(uid))
                FadeIn(uid, audio, seconds);
        }
    }
}
