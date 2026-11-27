using System.Linq;
using Content.Client.Audio;
using Content.Shared._WG.Hope;
using Content.Shared.CCVar;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WG.Hope;

/// <summary>
/// Hymns heard near a hymn source, such as the Sultan's altar on Helios. While one has the listener, ambient music
/// fades out and is held off; the hymn swells as they come near and fades as they walk away. It gives way to combat
/// music rather than holding it off.
/// </summary>
public sealed class WGHymnSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ContentAudioSystem _music = default!;

    /// <summary>Each hymn swells in over this long from its start, and away over this long before its end.</summary>
    private const float PieceFade = 4f;

    /// <summary>How long a hymn and an ambient track take to cross over, either way.</summary>
    private const float HandOver = 4f;

    private const float Silent = -80f;

    private WGHymnPrototype? _hymn;
    private EntityUid? _stream;
    private TimeSpan _started;
    private TimeSpan _next;
    private float _fade;
    private float _musicVolume = 1f;
    private bool _holding;
    private TimeSpan? _pauseAt;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, CCVars.AmbientMusicVolume, v => _musicVolume = v, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Stop();
        Release();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var now = _timing.CurTime;
        var (source, near, gap) = Listen();
        var want = source != null && !_music.WgHymnCombatPlaying;

        if (_hymn != null && now >= _started + TimeSpan.FromSeconds(_hymn.Seconds))
        {
            Stop();
            _next = now + TimeSpan.FromSeconds(_random.NextFloat(gap.X, MathF.Max(gap.X, gap.Y)));
        }

        // While a hymn has the listener, between hymns too, music is faded out and then held off.
        if (want && !_holding)
        {
            _holding = true;
            _music.WgHymnFadeOutMusic(HandOver);
            _pauseAt = now + TimeSpan.FromSeconds(HandOver - 0.1);
        }
        else if (!want && _holding && _hymn == null)
        {
            Release();
        }

        if (_holding && _pauseAt is { } pauseAt && now >= pauseAt)
        {
            _pauseAt = null;
            _music.WgHymnHoldMusic();
        }
        else if (_holding && _pauseAt == null)
        {
            // The playlist starts its next track on its own; hold every new one paused too.
            _music.WgHymnEnforceHold();
        }

        _fade = Math.Clamp(_fade + (want ? frameTime / 3f : -frameTime / HandOver), 0f, 1f);
        if (_hymn == null)
        {
            if (want && now >= _next)
                Start(source!);
            return;
        }

        if (_fade <= 0f)
        {
            Stop();
            Release();
            return;
        }

        var elapsed = (float) (now - _started).TotalSeconds;
        var piece = Smooth(MathF.Min(elapsed, _hymn.Seconds - elapsed) / PieceFade);
        // Faint at the edge of the source's reach, full beside it.
        var volume = -12f + 14f * near + 20f * MathF.Log10(MathF.Max(0.001f, _musicVolume * _fade * piece));
        // An internet sound or the soundtrack mute silences hymns too.
        if (_music.WgHymnSilenced)
            volume = Silent;
        _audio.SetVolume(_stream, volume);
    }

    /// <summary>The hymns the listener's nearest source sings, how near it is, and its quiet between hymns.</summary>
    private (string? Source, float Near, System.Numerics.Vector2 Gap) Listen()
    {
        if (_player.LocalEntity is not { } me || !TryComp<WGHymnListenerComponent>(me, out var listener)
            || listener.Source == null)
            return (null, 0f, default);

        return (listener.Source, listener.Near, listener.Gap);
    }

    private void Start(string source)
    {
        var pool = _proto.EnumeratePrototypes<WGHymnPrototype>().Where(h => h.Source == source).ToList();
        if (pool.Count == 0)
            return;

        var pick = _random.NextFloat(0f, pool.Sum(h => h.Weight));
        _hymn = pool.First();
        foreach (var hymn in pool)
        {
            pick -= hymn.Weight;
            if (pick <= 0f)
            {
                _hymn = hymn;
                break;
            }
        }

        _started = _timing.CurTime;
        _stream = _audio.PlayGlobal(new SoundPathSpecifier(_hymn.Path), Filter.Local(), false,
            AudioParams.Default.WithVolume(Silent))?.Entity;
    }

    private void Stop()
    {
        _audio.Stop(_stream);
        _stream = null;
        _hymn = null;
    }

    /// <summary>Lets the music go: it fades back in where it was held, or starts again on its own.</summary>
    private void Release()
    {
        if (!_holding)
            return;

        _holding = false;
        _pauseAt = null;
        _music.WgHymnReleaseMusic(HandOver);
    }

    private static float Smooth(float u)
    {
        u = Math.Clamp(u, 0f, 1f);
        return u * u * (3f - 2f * u);
    }
}
