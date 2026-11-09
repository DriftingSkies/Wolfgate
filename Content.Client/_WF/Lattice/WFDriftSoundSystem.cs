using System.Numerics;
using Content.Shared._WF.Lattice;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Shared;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// What Drift does to what this player hears. In the Detune band sounds go dull and distant. An echo makes sounds round
/// them play again a moment later from the wrong side. A silence cuts every sound out, then plays everything they missed
/// at once.
/// </summary>
public sealed class WFDriftSoundSystem : EntitySystem
{
    [Dependency] private IAudioManager _audioManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How much quieter the world sounds in the Detune band.</summary>
    private const float DetuneGain = 0.75f;

    /// <summary>Chance a sound echoes while an echo lasts.</summary>
    private const float EchoChance = 0.4f;

    /// <summary>The most missed sounds a silence plays back at once.</summary>
    private const int MissedMax = 14;

    private readonly HashSet<EntityUid> _heard = new();
    private readonly HashSet<EntityUid> _ours = new();
    private readonly List<(string File, AudioParams Params, MapCoordinates? At, TimeSpan When)> _echoes = new();
    private readonly List<(string File, AudioParams Params, MapCoordinates? At)> _missed = new();
    private TimeSpan _echoUntil;
    private TimeSpan _silenceUntil;
    private readonly HashSet<EntityUid> _alive = new();
    private bool _silent;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFLatticeHallucinationEvent>(OnHallucination);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _audioManager.SetMasterGain(_cfg.GetCVar(CVars.AudioMasterVolume));
    }

    private void OnHallucination(WFLatticeHallucinationEvent ev)
    {
        var until = _timing.CurTime + TimeSpan.FromSeconds(ev.Seconds);
        switch (ev.Kind)
        {
            case WFLatticeHallucinationKind.Echo:
                _echoUntil = until;
                break;
            case WFLatticeHallucinationKind.Silence:
                _silenceUntil = until;
                _silent = true;
                _missed.Clear();
                break;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.CurTime;
        Listen(now);
        PlayEchoes(now);

        if (_silent && now >= _silenceUntil)
        {
            _silent = false;
            ApplyGain();
            // Everything they missed, all at once.
            foreach (var (file, audioParams, at) in _missed)
            {
                Play(file, audioParams, at);
            }

            _missed.Clear();
        }

        ApplyGain();
    }

    /// <summary>Notes each new sound: during a silence it is missed, during an echo it may come again.</summary>
    private void Listen(TimeSpan now)
    {
        var echoing = now < _echoUntil;
        var query = EntityQueryEnumerator<AudioComponent>();
        _alive.Clear();
        while (query.MoveNext(out var uid, out var audio))
        {
            _alive.Add(uid);
            if (!_heard.Add(uid) || _ours.Contains(uid) || string.IsNullOrEmpty(audio.FileName) || audio.Params.Loop)
                continue;

            MapCoordinates? at = audio.Global ? null : _transform.GetMapCoordinates(uid);
            if (_silent)
            {
                if (_missed.Count < MissedMax)
                    _missed.Add((audio.FileName, audio.Params, at));
                continue;
            }

            if (echoing && _random.Prob(EchoChance))
                _echoes.Add((audio.FileName, audio.Params, Mirror(at), now + TimeSpan.FromSeconds(_random.NextFloat(0.3f, 1.1f))));
        }

        _heard.IntersectWith(_alive);
        _ours.IntersectWith(_alive);
    }

    private void PlayEchoes(TimeSpan now)
    {
        for (var i = _echoes.Count - 1; i >= 0; i--)
        {
            var (file, audioParams, at, when) = _echoes[i];
            if (now < when)
                continue;

            _echoes.RemoveAt(i);
            Play(file, audioParams.WithVolume(audioParams.Volume - 3f), at);
        }
    }

    /// <summary>The same sound, from the other side of the listener.</summary>
    private MapCoordinates? Mirror(MapCoordinates? at)
    {
        if (at is not { } point || _player.LocalEntity is not { } local)
            return at;

        var centre = _transform.GetMapCoordinates(local);
        if (centre.MapId != point.MapId || !_maps.MapExists(point.MapId))
            return at;

        return new MapCoordinates(centre.Position * 2f - point.Position, point.MapId);
    }

    private void Play(string file, AudioParams audioParams, MapCoordinates? at)
    {
        var sound = new SoundPathSpecifier(file);
        // A sound heard on a map that has since gone is dropped, not played from nowhere.
        if (at is { } point && !_maps.MapExists(point.MapId))
            return;

        var played = at is { } place && _transform.ToCoordinates(place) is var coords && coords.IsValid(EntityManager)
            ? _audio.PlayStatic(sound, Filter.Local(), coords, false, audioParams)
            : _audio.PlayGlobal(sound, Filter.Local(), false, audioParams);

        if (played is { } entity)
            _ours.Add(entity.Entity);
    }

    /// <summary>Silence mutes everything; the Detune band dulls it; otherwise the player's own volume.</summary>
    private void ApplyGain()
    {
        var gain = _cfg.GetCVar(CVars.AudioMasterVolume);
        if (_silent)
            gain = 0f;
        else if (InDetune())
            gain *= DetuneGain;

        if (!MathHelper.CloseTo(gain, _audioManager.BaseGain))
            _audioManager.SetMasterGain(gain);
    }

    private bool InDetune()
    {
        var drift = EntityManager.System<WFLatticeHallucinationSystem>().LocalDrift();
        return drift is { Band: 1 } && _player.LocalEntity is { } local && !HasComp<WFChoirMemberComponent>(local);
    }
}
