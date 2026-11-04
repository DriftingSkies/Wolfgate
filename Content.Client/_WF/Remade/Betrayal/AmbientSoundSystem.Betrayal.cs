using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Client.Audio;

/// <summary>A partial so the ambient system stays untouched: the Remade silence scales every ambient loop.</summary>
public sealed partial class AmbientSoundSystem
{
    private float _wfBetrayalLast = 1f;

    /// <summary>
    /// Scales every playing ambient loop to a level from 0 to 1. Called every frame while the level is under 1, so loops that start
    /// in the silence are caught, and once more as it returns, which puts them back at their own volume.
    /// </summary>
    public void SilenceWolfgate(float level)
    {
        var last = _wfBetrayalLast;
        _wfBetrayalLast = level;
        if (level >= 1f && last >= 1f)
            return;

        foreach (var (_, values) in _playingSounds)
        {
            if (values.Stream is not { } stream || !TryComp<AudioComponent>(stream, out var audio))
                continue;

            audio.Gain = SharedAudioSystem.VolumeToGain(audio.Params.Volume) * MathF.Max(level, 0f);
        }
    }
}
