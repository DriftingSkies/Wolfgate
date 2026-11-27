using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;

namespace Content.Client.Audio;

/// <summary>
/// What a hymn needs from the music: to fade it out, hold it paused, and fade it back in. It keeps its own hold rather
/// than a <see cref="WgMusicPauseReason"/>, and hands what it held to those reasons if one still holds the music when
/// it lets go. A partial so the music systems stay untouched.
/// </summary>
public sealed partial class ContentAudioSystem
{
    /// <summary>What silences a hymn as well as the music: an internet sound, the soundtrack mute or a combat preview.</summary>
    private const WgMusicPauseReason WgHymnSilencers =
        WgMusicPauseReason.InternetSound | WgMusicPauseReason.SoundtrackMute | WgMusicPauseReason.CombatMusicPreview;

    private bool _wgHymnHolding;
    private readonly HashSet<EntityUid> _wgHymnPaused = new();

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

    /// <summary>Holds the music paused for a hymn, and keeps any new track paused until it lets go.</summary>
    public void WgHymnHoldMusic()
    {
        _wgHymnHolding = true;
        WgHymnEnforceHold();
    }

    /// <summary>Pauses tracks the music loops started since the last call. Run every frame while the hymn holds.</summary>
    public void WgHymnEnforceHold()
    {
        if (!_wgHymnHolding)
            return;

        WgHymnPause(_ambientMusicStream);
        WgHymnPause(_lobbySoundtrackInfo?.MusicStreamEntityUid);
    }

    /// <summary>
    /// Lets the music go: what the hymn held fades back in, or stays paused under any reason that still holds it.
    /// </summary>
    public void WgHymnReleaseMusic(float seconds)
    {
        _wgHymnHolding = false;
        foreach (var uid in _wgHymnPaused)
        {
            if (!TryComp<AudioComponent>(uid, out var audio) || audio.State != AudioState.Paused)
                continue;

            if (_wgMusicPauseReasons != WgMusicPauseReason.None)
            {
                _wgPausedMusic.Add(uid);
                continue;
            }

            _audio.SetState(uid, AudioState.Playing, component: audio);
            if (!_fadingIn.ContainsKey(uid))
                FadeIn(uid, audio, seconds);
        }

        _wgHymnPaused.Clear();
    }

    private void WgHymnPause(EntityUid? uid)
    {
        if (uid == null || !TryComp<AudioComponent>(uid, out var audio))
            return;

        // The engine starts a new track on its first audio frame even if it was paused before then, leaving it marked
        // paused but audible; re-pause those too.
        var restarted = audio.State == AudioState.Paused && audio.Playing;
        if (audio.State != AudioState.Playing && !restarted)
            return;

        _audio.SetState(uid, AudioState.Paused, force: restarted, component: audio);
        _wgHymnPaused.Add(uid.Value);
    }
}
