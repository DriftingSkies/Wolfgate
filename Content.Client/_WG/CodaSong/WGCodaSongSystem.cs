using System.Linq;
using System.Numerics;
using Content.Client.Audio;
using Content.Shared._WG.CodaSong;
using Content.Shared._WG.Lattice;
using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WG.CodaSong;

/// <summary>
/// The song the Coda sings in a drifting mind. Every part of a piece plays at once, clean and turned, and the mix
/// follows the listener: a lone soprano, faint and far off, at low Drift; more voices and nearer as it climbs; parts
/// turning one by one from the middle of the scale; and at the very top the clearest piece of all. A fragment's heart
/// sings the same way to anyone near it, its singers turning as the heart is hurt. The dead in the Coda have joined
/// the choir: a ghost there hears every voice, clear and close. Pieces end in silence, and the next one comes after a
/// while; no two listeners hear the same mix. In a fight it blends under the combat music, ducking and turning as the
/// listener nears death, until in crisis only one clear voice is left.
/// </summary>
public sealed partial class WGCodaSongSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ContentAudioSystem _music = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;

    /// <summary>The order voices come in as the pull grows: the soprano first, the profondo last.</summary>
    private static readonly string[] Order = { "soprano", "alto", "tenor", "bass", "profondo" };

    /// <summary>Each piece swells in over this long from its start, and away over this long before its end.</summary>
    private const float PieceFade = 5f;

    /// <summary>Below this pull nothing sings.</summary>
    private const float Threshold = 0.08f;

    private const float Silent = -80f;

    private sealed class Voice
    {
        public EntityUid? Clean;
        public EntityUid? Turned;
        public float Gain;
        public float TurnedGain;
    }

    private WGCodaSongPrototype? _song;
    private readonly Dictionary<string, Voice> _voices = new();
    private readonly List<(Vector2 Position, float Strength)> _anchors = new();
    private TimeSpan _started;
    private TimeSpan _next;
    private float _fade;
    private float _musicVolume = 1f;
    private bool _holding;
    private TimeSpan? _pauseAt;
    private bool _cutShort;

    /// <summary>How long the song and an ambient track take to cross over, either way.</summary>
    private const float HandOver = 4f;
    private TimeSpan _disturbedUntil;
    private float _disturbance;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, CCVars.AmbientMusicVolume, v => _musicVolume = v, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        StopSong();
    }

    /// <summary>Turns every singer for a moment, as when a wave of warped space passes through the listener.</summary>
    public void Disturb(float seconds, float amount)
    {
        _disturbedUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        _disturbance = MathF.Max(_disturbance, amount);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var now = _timing.CurTime;
        var (pull, turn, source, drift) = Listen();

        // The song outranks every ambient track, a Lattice band's own piece included; a heart's outranks combat too.
        var heart = source != null;

        // Reaching the top of the scale mid-piece: that piece ebbs away now, and the finality follows soon after.
        if (_song is { Source: null } && !_cutShort && drift >= 90f && _song.MinDrift < 85f)
            CutShort(now);

        // A heart coming into reach, another taking over, or the listener leaving one: the piece playing ebbs away and
        // the one that fits follows soon after.
        if (_song != null && !_cutShort && _song.Source != source)
            CutShort(now);

        if (_song != null && now >= _started + TimeSpan.FromSeconds(_song.Seconds))
        {
            var quick = _cutShort;
            StopSong();
            _next = now + TimeSpan.FromSeconds(quick ? _random.NextFloat(1f, 3f)
                : _random.NextFloat(heart ? 6f : 20f, heart ? 15f : 60f));
        }

        // While the song has the listener, between its pieces too, music is faded out and then held off. Combat music
        // is blended with instead, unless a heart is singing. Once the song has faded away, the music fades back in.
        var want = (pull >= Threshold || _song != null) && (heart || !_music.WgCombatMusicPlaying);
        if (want && !_holding)
        {
            _holding = true;
            _music.WgCodaSongHolding = true;
            _music.FadeOutMusicWolfgate(HandOver);
            _pauseAt = now + TimeSpan.FromSeconds(HandOver - 0.1);
        }
        else if (!want && _holding)
        {
            Release();
        }

        if (_holding && _pauseAt is { } pauseAt && now >= pauseAt)
        {
            _pauseAt = null;
            _music.PauseMusicWolfgate(WgMusicPauseReason.CodaSong);
        }
        else if (_holding && _pauseAt == null)
        {
            // The playlist starts its next track on its own; hold every new one paused too.
            _music.EnforceMusicPauseWolfgate();
        }

        // Fade in while something pulls; fade out and stop once nothing does.
        _fade = Math.Clamp(_fade + (pull >= Threshold ? frameTime / 3f : -frameTime / HandOver), 0f, 1f);
        if (_song == null)
        {
            if (pull >= Threshold && now >= _next)
                StartSong(source, drift);
            return;
        }

        if (_fade <= 0f)
        {
            StopSong();
            return;
        }

        if (now < _disturbedUntil)
            turn = MathF.Max(turn, _disturbance);
        else
            _disturbance = 0f;

        // Near death the choir goes wrong; in crisis every voice but one falls away, and that one sings clear.
        var (danger, crisis) = Danger();
        if (crisis)
            turn = 0f;
        else
            turn = MathF.Max(turn, danger * 0.9f);

        Mix(pull, turn, drift, frameTime, danger, crisis, heart);
    }

    /// <summary>Brings the piece playing to its closing fade now, from wherever its fade stands, without a jump.</summary>
    private void CutShort(TimeSpan now)
    {
        if (_song == null)
            return;

        _cutShort = true;
        var elapsed = (float) (now - _started).TotalSeconds;
        var remaining = MathF.Min(PieceFade, MathF.Min(elapsed, _song.Seconds - elapsed));
        var cut = now - TimeSpan.FromSeconds(_song.Seconds - MathF.Max(0f, remaining));
        if (cut > _started)
            _started = cut;
    }

    /// <summary>Lets the music go: it fades back in where it was held, or starts again on its own.</summary>
    private void Release()
    {
        if (!_holding)
            return;

        _holding = false;
        _pauseAt = null;
        _music.WgCodaSongHolding = false;
        _music.ResumeMusicFadingWolfgate(WgMusicPauseReason.CodaSong, HandOver);
    }

    /// <summary>How close the listener is to falling, 0 to 1, and whether they already have.</summary>
    private (float Danger, bool Crisis) Danger()
    {
        if (_player.LocalEntity is not { } me || !TryComp<MobStateComponent>(me, out var state))
            return (0f, false);

        if (state.CurrentState == MobState.Critical)
            return (1f, true);

        if (state.CurrentState != MobState.Alive || !TryComp<DamageableComponent>(me, out var damage)
            || !_thresholds.TryGetIncapThreshold(me, out var incap) || incap.Value <= 0)
            return (0f, false);

        return (Math.Clamp(damage.TotalDamage.Float() / incap.Value.Float(), 0f, 1f), false);
    }

    /// <summary>How hard the song pulls at the listener, how far its singers have turned, and why.</summary>
    private (float Pull, float Turn, string? Source, float Drift) Listen()
    {
        if (_player.LocalEntity is not { } me)
            return (0f, 0f, null, 0f);

        // The dead in the Coda have joined the choir: every voice, clear and close, from any piece.
        if (HasComp<GhostComponent>(me) && InCoda(me))
            return (1f, 0f, null, -1f);

        // The client only sees Drift in steps: the band's, and what the listener carries in their head.
        var drift = TryComp<WGDriftComponent>(me, out var mind) ? Math.Max(Math.Max(mind.Level, mind.MindLevel), mind.FeltLevel) * WGDriftComponent.LevelStep : 0f;
        var pull = Math.Clamp((drift - 8f) / 70f, 0f, 1f);
        // The middle of the scale turns the singers one by one; past 90 they come back, clear.
        var turn = drift >= 90f ? 0f : Math.Clamp((drift - 55f) / 30f, 0f, 1f);
        string? source = null;

        // A heart in reach, as the server reports it: it takes the listener over, near or far.
        if (TryComp<WGSongListenerComponent>(me, out var listener) && listener.Source != null)
        {
            pull = MathF.Max(pull, listener.Near);
            source = listener.Source;
            turn = MathF.Max(turn, listener.Turned);
        }

        return (pull, turn, source, drift);
    }

    /// <summary>Whether an entity is in the Coda's hazard bands, worked out from its map's Lattice field.</summary>
    private bool InCoda(EntityUid uid)
    {
        if (Transform(uid).MapUid is not { } map || !TryComp<WGLatticeFieldComponent>(map, out var field)
            || !_proto.TryIndex(field.Profile, out var profile) || field.Thresholds.Count == 0)
            return false;

        _anchors.Clear();
        for (var i = 0; i < field.Anchors.Count && i < field.Strengths.Count; i++)
        {
            _anchors.Add((field.Anchors[i], field.Strengths[i]));
        }

        var strength = WGLatticeMath.Field(_transform.GetWorldPosition(uid), _anchors, profile.Reach);
        return WGLatticeMath.Band(strength, field.Thresholds) >= profile.HazardBand;
    }

    private void StartSong(string? source, float drift)
    {
        // A drift below zero is a ghost's: any piece the Coda sings to the drifting will do.
        var pool = _proto.EnumeratePrototypes<WGCodaSongPrototype>()
            .Where(s => source != null ? s.Source == source
                : s.Source == null && (drift < 0f || drift >= s.MinDrift && drift <= s.MaxDrift))
            .ToList();
        if (pool.Count == 0)
            return;

        var pick = _random.NextFloat(0f, pool.Sum(s => s.Weight));
        _song = pool.First();
        foreach (var song in pool)
        {
            pick -= song.Weight;
            if (pick <= 0f)
            {
                _song = song;
                break;
            }
        }

        _started = _timing.CurTime;
        foreach (var (part, path) in _song.Stems)
        {
            var voice = new Voice { Clean = Play(path) };
            if (_song.Turned.TryGetValue(part, out var turned))
                voice.Turned = Play(turned);

            _voices[part] = voice;
        }
    }

    private EntityUid? Play(Robust.Shared.Utility.ResPath path)
    {
        return _audio.PlayGlobal(new SoundPathSpecifier(path), Filter.Local(), false, AudioParams.Default.WithVolume(Silent))?.Entity;
    }

    private void StopSong()
    {
        foreach (var voice in _voices.Values)
        {
            _audio.Stop(voice.Clean);
            _audio.Stop(voice.Turned);
        }

        _voices.Clear();
        _song = null;
        _cutShort = false;
    }

    /// <summary>Sets every stem's volume from the pull, the turn and each part's place in the order.</summary>
    private void Mix(float pull, float turn, float drift, float frameTime, float danger, bool crisis, bool heart)
    {
        // Under combat music the choir sits back, and sinks further the closer the listener is to death; a heart's
        // song holds combat music off instead.
        var combat = _music.WgCombatMusicPlaying && !heart;
        var duck = combat ? -2f - 8f * danger : -5f * danger;
        // Each piece swells in from nothing and ebbs away before it ends, so no piece starts or stops abruptly.
        var elapsed = (float) (_timing.CurTime - _started).TotalSeconds;
        var left = _song != null ? _song.Seconds - elapsed : PieceFade;
        var piece = Smooth(MathF.Min(elapsed, left) / PieceFade);
        // Far off and faint at first, near and full at the top.
        var master = (-10f + 16f * pull) + duck + 20f * MathF.Log10(MathF.Max(0.001f, _musicVolume * _fade * piece));
        if (crisis)
            master += combat ? 10f : 6f;
        // Ambient music turned all the way down silences the choir, crisis or not.
        if (_musicVolume <= 0f)
            master = Silent;
        // Whatever else holds the music, an internet sound or the soundtrack mute, silences the choir too. The stems
        // keep running muted so they stay together, and come back as soon as nothing holds them.
        if (_music.WgMusicHeldElsewhere)
            master = Silent;
        for (var i = 0; i < Order.Length; i++)
        {
            if (!_voices.TryGetValue(Order[i], out var voice))
                continue;

            // Each voice comes in at its own point in the pull, the lowest last; in crisis only the soprano stays.
            var target = crisis ? (i == 0 ? 1f : 0f) : Math.Clamp((pull - i * 0.15f) / 0.12f, 0f, 1f);
            // And each turns at its own point, the soprano last.
            var turned = voice.Turned != null ? Math.Clamp(turn * 1.4f - (Order.Length - 1 - i) * 0.1f, 0f, 1f) : 0f;
            voice.Gain += (target - voice.Gain) * MathF.Min(1f, frameTime * 0.8f);
            voice.TurnedGain += (turned - voice.TurnedGain) * MathF.Min(1f, frameTime * 1.5f);
            _audio.SetVolume(voice.Clean, Decibels(voice.Gain * (1f - voice.TurnedGain), master));
            _audio.SetVolume(voice.Turned, Decibels(voice.Gain * voice.TurnedGain, master));
        }
    }

    private static float Smooth(float u)
    {
        u = Math.Clamp(u, 0f, 1f);
        return u * u * (3f - 2f * u);
    }

    private static float Decibels(float gain, float master)
    {
        return gain <= 0.001f ? Silent : master + 20f * MathF.Log10(gain);
    }
}
