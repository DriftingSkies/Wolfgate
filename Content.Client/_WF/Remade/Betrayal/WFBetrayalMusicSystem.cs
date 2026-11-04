using Content.Client._WF.Remade.Dread;
using Content.Client.Audio;
using Content.Client.Gameplay;
using Content.Shared._WF.Remade.Betrayal;
using Content.Shared.CCVar;
using Robust.Client.State;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WF.Remade.Betrayal;

/// <summary>
/// The music of the dread, in this player's head alone. A silence fades the ambient sound, the music and the whisper bed to nothing
/// when the Director goes quiet, and holds until a stinger breaks it or its time runs out. A score of up to five low layers rises
/// with the stone's resonance. Nothing plays in the lobby, with <c>wf.remade.betrayal.music</c> at 0 or with the music slider off.
/// </summary>
public sealed partial class WFBetrayalMusicSystem : EntitySystem
{
    [Dependency] private AmbientSoundSystem _ambient = default!;
    [Dependency] private ContentAudioSystem _music = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WFDreadBedSystem _bed = default!;

    private const float FadeOut = 1.5f;
    private const float FadeIn = 4f;
    private const float EaseRate = 0.5f;
    private const float GainRate = 1.2f;
    private const float Silent = 0.004f;
    private const float LayerGain = 0.55f;

    private readonly EntityUid?[] _streams = new EntityUid?[WFBetrayalSounds.ScoreLayers.Length];
    private readonly float[] _gains = new float[WFBetrayalSounds.ScoreLayers.Length];
    private TimeSpan _silenceUntil;
    private TimeSpan _scoreHold;
    private float _level = 1f;
    private int _layers;
    private float _scoreGain;
    private bool _silenceApplied;

    /// <summary>How much of the ambient sound is left, from 0 to 1. Tests read it.</summary>
    public float Level => _level;

    /// <summary>Stingers played and score streams open. Tests read them.</summary>
    public int Stingers { get; private set; }

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
        SubscribeNetworkEvent<WFBetrayalSilenceEvent>(OnSilence);
        SubscribeNetworkEvent<WFBetrayalStingerEvent>(OnStinger);
        SubscribeNetworkEvent<WFBetrayalScoreEvent>(OnScore);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Reset());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Reset();
    }

    /// <summary>Stops the score and lifts any silence at once.</summary>
    public void Reset()
    {
        _silenceUntil = TimeSpan.Zero;
        _layers = 0;
        _scoreGain = 0f;
        StopScore();
        Restore();
    }

    private void StopScore()
    {
        for (var i = 0; i < _streams.Length; i++)
        {
            _streams[i] = _audio.Stop(_streams[i]);
            _gains[i] = 0f;
        }
    }

    /// <summary>Puts the ambient sound, the music and the bed back as they were.</summary>
    private void Restore()
    {
        _level = 1f;
        if (!_silenceApplied)
            return;

        _silenceApplied = false;
        _ambient.SilenceWolfgate(1f);
        _music.SilenceMusicWolfgate(1f);
        _bed.BetrayalSilence = 1f;
    }

    /// <summary>The music slider times this client's setting: how loud the stingers and score may be, from 0.</summary>
    private float Master()
    {
        if (_state.CurrentState is not GameplayState)
            return 0f;

        return Math.Clamp(_config.GetCVar(WFBetrayalCVars.Music), 0f, 1f) * Math.Clamp(_config.GetCVar(CCVars.AmbientMusicVolume), 0f, 1f);
    }

    private void OnSilence(WFBetrayalSilenceEvent ev)
    {
        if (Master() <= 0f)
            return;

        _silenceUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Clamp(ev.Seconds, 1f, 60f));
    }

    private void OnStinger(WFBetrayalStingerEvent ev)
    {
        _silenceUntil = TimeSpan.Zero;
        var master = Master();
        if (master <= 0f || ev.Stinger < 0 || ev.Stinger >= WFBetrayalSounds.Stingers.Length)
            return;

        _audio.PlayGlobal(new SoundPathSpecifier(WFBetrayalSounds.Stingers[ev.Stinger]), Filter.Local(), false,
            AudioParams.Default.WithVolume(-1f + SharedAudioSystem.GainToVolume(Math.Max(master * 2f, 0.05f))));
        Stingers++;
    }

    private void OnScore(WFBetrayalScoreEvent ev)
    {
        _layers = Math.Clamp(ev.Layers, 0, _streams.Length);
        _scoreGain = Math.Clamp(ev.Gain, 0f, 1f);
        _scoreHold = _timing.CurTime + TimeSpan.FromSeconds(Math.Clamp(ev.Seconds, 0.5f, 30f));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var master = Master();
        var silent = _timing.CurTime < _silenceUntil && master > 0f;
        var wanted = silent ? 0f : 1f;
        var rate = silent ? 1f / FadeOut : 1f / FadeIn;
        _level = wanted < _level ? Math.Max(wanted, _level - frameTime * rate) : Math.Min(wanted, _level + frameTime * rate);

        if (_level < 1f)
        {
            _silenceApplied = true;
            _ambient.SilenceWolfgate(_level);
            _music.SilenceMusicWolfgate(_level);
            _bed.BetrayalSilence = _level;
        }
        else if (_silenceApplied)
        {
            Restore();
        }

        UpdateScore(frameTime, master);
    }

    private void UpdateScore(float frameTime, float master)
    {
        // Nothing renewing the score lets it fade out.
        if (_timing.CurTime > _scoreHold)
            _layers = 0;

        if (_layers == 0 && Playing == 0)
            return;

        // One track at a time: each step of resonance crossfades to the next, darker one rather than stacking songs.
        for (var i = 0; i < _streams.Length; i++)
        {
            var target = i == _layers - 1 ? LayerGain * _scoreGain * master * _level : 0f;
            Ease(i, target, frameTime);
        }
    }

    /// <summary>Moves one layer's loudness towards what it should be, opening its stream when it first becomes audible.</summary>
    private void Ease(int layer, float target, float frameTime)
    {
        if (_streams[layer] is { } open && !HasComp<AudioComponent>(open))
            _streams[layer] = null;

        _gains[layer] += (target - _gains[layer]) * Math.Min(1f, frameTime * GainRate * (target > _gains[layer] ? EaseRate : 2f));

        if (_gains[layer] <= Silent && target <= Silent)
        {
            _streams[layer] = _audio.Stop(_streams[layer]);
            _gains[layer] = 0f;
            return;
        }

        if (_streams[layer] == null)
        {
            var stream = _audio.PlayGlobal(new SoundPathSpecifier(WFBetrayalSounds.ScoreLayers[layer]), Filter.Local(), false,
                AudioParams.Default.WithLoop(true).WithVolume(-20f).WithPlayOffset(_random.NextFloat(0f, 30f)));
            _streams[layer] = stream?.Entity;
        }

        _audio.SetGain(_streams[layer], _gains[layer]);
    }
}
