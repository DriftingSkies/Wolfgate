using Content.Shared._WF.CCVar;
using Content.Shared.Audio.Jukebox;
using Robust.Client.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;

namespace Content.Client._WF.JukeboxVolume;

/// <summary>Scales jukebox songs by the client's jukebox volume setting.</summary>
public sealed partial class JukeboxVolumeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private float _volume = 1f;

    /// <summary>The gain this system last set on each stream, so a frame the engine skips isn't scaled twice.</summary>
    private Dictionary<EntityUid, float> _scaled = new();
    private Dictionary<EntityUid, float> _scaledNext = new();

    public override void Initialize()
    {
        base.Initialize();

        // The engine resets each stream's gain on the frames it processes audio.
        UpdatesAfter.Add(typeof(AudioSystem));

        Subs.CVar(_cfg, JukeboxVolumeCVars.Volume, value => _volume = value, true);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _scaledNext.Clear();
        if (_volume < 1f)
        {
            var query = EntityQueryEnumerator<JukeboxComponent>();
            while (query.MoveNext(out _, out var jukebox))
            {
                if (jukebox.AudioStream is not { } stream || !TryComp<AudioComponent>(stream, out var audio))
                    continue;

                // Still the gain set last frame: the engine hasn't reset it since.
                if (!_scaled.TryGetValue(stream, out var last) || audio.Gain != last)
                    audio.Gain *= _volume;

                _scaledNext[stream] = audio.Gain;
            }
        }

        (_scaled, _scaledNext) = (_scaledNext, _scaled);
    }
}
