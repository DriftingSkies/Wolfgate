using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Client.Audio;

/// <summary>A partial so the music systems stay untouched: the Remade silence scales the ambient track's loudness.</summary>
public sealed partial class ContentAudioSystem
{
    private float _wfBetrayalLast = 1f;

    /// <summary>
    /// Scales the ambient and combat music track, and any track fading out, to a level from 0 to 1. Called every frame while the
    /// level is under 1 and once more as it returns, which puts the track back at its own volume.
    /// </summary>
    public void SilenceMusicWolfgate(float level)
    {
        var last = _wfBetrayalLast;
        _wfBetrayalLast = level;

        foreach (var fading in _fadingOut.Keys)
        {
            if (TryComp<AudioComponent>(fading, out var fadingAudio))
                ScaleMusicGain(fadingAudio, level);
        }

        if (_ambientMusicStream is not { } stream || !TryComp<AudioComponent>(stream, out var audio))
            return;

        if (level >= 1f)
        {
            if (last < 1f && audio.Playing)
                audio.Gain = SharedAudioSystem.VolumeToGain(audio.Params.Volume);

            return;
        }

        ScaleMusicGain(audio, level);
    }
}
