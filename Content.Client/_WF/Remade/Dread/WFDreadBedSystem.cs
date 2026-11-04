using Content.Shared._WF.Remade.Dread;
using Content.Shared.CCVar;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WF.Remade.Dread;

/// <summary>
/// The whisper bed: a low, continuous murmur layered under a player's own head while a Monolith's influence reaches them, from
/// Harvest. Three whisper loops of different lengths, so they never line up, come in one after another as the bed rises, then a
/// heartbeat and a drone. The server sends the bed's level with the warp of sight and keeps renewing it; when it stops, the bed
/// fades over a few seconds. Muting ambience or setting <c>wf.remade.dread.bed</c> to 0 turns it off.
/// </summary>
public sealed partial class WFDreadBedSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    /// <summary>The layers, in the order <see cref="WFDreadMath.BedLayers"/> gives them.</summary>
    public static readonly string[] Layers =
    {
        "/Audio/_WF/Remade/dread/bed_whisper_a.ogg",
        "/Audio/_WF/Remade/dread/bed_whisper_b.ogg",
        "/Audio/_WF/Remade/dread/bed_whisper_c.ogg",
        "/Audio/_WF/Remade/dread/menace_heart_beat.ogg",
        "/Audio/_WF/Remade/dread/bed_drone.ogg",
    };

    private const float FadeSeconds = 5f;
    private const float EaseRate = 0.6f;
    private const float GainRate = 1.5f;
    private const float Silent = 0.004f;

    /// <summary>Decibels under the music the bed sits at: it's a texture under the score, not a voice.</summary>
    private const float Trim = -8f;

    private readonly EntityUid?[] _streams = new EntityUid?[Layers.Length];
    private readonly float[] _gains = new float[Layers.Length];
    private float _target;
    private float _bed;
    private TimeSpan _holdUntil;

    /// <summary>The bed level being played now. Tests read it.</summary>
    public float Bed => _bed;

    /// <summary>How many layers have a stream open. Tests read it.</summary>
    public int Playing
    {
        get
        {
            var count = 0;
            foreach (var stream in _streams)
            {
                if (stream != null)
                    count++;
            }

            return count;
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFDreadVisionEvent>(OnVision);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Reset());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        StopAll();
    }

    /// <summary>Stops the bed at once.</summary>
    public void Reset()
    {
        _target = 0f;
        _bed = 0f;
        StopAll();
    }

    private void StopAll()
    {
        for (var i = 0; i < _streams.Length; i++)
        {
            _streams[i] = _audio.Stop(_streams[i]);
            _gains[i] = 0f;
        }
    }

    private void OnVision(WFDreadVisionEvent ev)
    {
        _target = Math.Clamp(ev.Bed, 0f, 1f);
        _holdUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Clamp(ev.Seconds, 0.5f, 30f));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_target <= 0f && _bed <= 0.001f && Playing == 0)
            return;

        // Nothing renewing the level lets it fade out.
        if (_timing.CurTime > _holdUntil)
            _target = Math.Max(0f, _target - frameTime / FadeSeconds);

        _bed += (_target - _bed) * Math.Min(1f, frameTime * EaseRate);

        var master = _config.GetCVar(WFDreadCVars.Bed);
        if (_config.GetCVar(CCVars.AmbienceVolume) <= 0.0001f)
            master = 0f;
        master *= BetrayalSilence;

        var wanted = WFDreadMath.BedLayers(_bed);
        Ease(0, wanted.WhisperA * master, frameTime);
        Ease(1, wanted.WhisperB * master, frameTime);
        Ease(2, wanted.WhisperC * master, frameTime);
        Ease(3, wanted.Heartbeat * master, frameTime);
        Ease(4, wanted.Drone * master, frameTime);
    }

    /// <summary>Moves one layer's loudness towards what it should be, opening its stream when it first becomes audible and closing it at silence.</summary>
    private void Ease(int layer, float target, float frameTime)
    {
        if (_streams[layer] is { } open && !HasComp<AudioComponent>(open))
            _streams[layer] = null;

        _gains[layer] += (target - _gains[layer]) * Math.Min(1f, frameTime * GainRate);

        if (_gains[layer] <= Silent && target <= Silent)
        {
            _streams[layer] = _audio.Stop(_streams[layer]);
            _gains[layer] = 0f;
            return;
        }

        if (_streams[layer] == null)
        {
            // A random start, so the loops don't begin in step.
            var stream = _audio.PlayGlobal(new SoundPathSpecifier(Layers[layer]), Filter.Local(), false,
                AudioParams.Default.WithLoop(true).WithVolume(-20f).WithPlayOffset(_random.NextFloat(0f, 8f)));
            _streams[layer] = stream?.Entity;
        }

        _audio.SetVolume(_streams[layer], Trim + SharedAudioSystem.GainToVolume(_gains[layer]));
    }
}
