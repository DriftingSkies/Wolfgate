using System.Numerics;
using Content.Shared._WF.Remade.Betrayal;
using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WF.Remade.Betrayal;

/// <summary>
/// False sounds, played to this player alone and placed by their own client: knocking from the far side of a door near them,
/// scraping inside a wall, breath just behind them, and footsteps that follow the way they walked and stop when they stop. The
/// server only says which and how hard. <c>wf.remade.betrayal.sounds</c> at 0 turns them off.
/// </summary>
public sealed partial class WFBetrayalSensesSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    private const float Lag = 1.2f;
    private const float StopSpeed = 0.3f;
    private const float StopAfter = 0.2f;
    private const float GiveUpAfter = 2.5f;
    private const float HistorySeconds = 4f;

    private readonly record struct Pending(TimeSpan At, SoundSpecifier Sound, MapCoordinates Where, float Volume);

    private readonly List<Pending> _pending = new();
    private readonly List<(TimeSpan At, Vector2 Position)> _trail = new();
    private readonly HashSet<Entity<DoorComponent>> _doors = new();
    private TimeSpan _stepsUntil;
    private TimeSpan _nextStep;
    private float _stepIntensity;
    private float _stopped;

    /// <summary>Sounds placed so far, by kind. Tests read them.</summary>
    public int Knocks { get; private set; }

    public int Scrapes { get; private set; }

    public int Breaths { get; private set; }

    public int Steps { get; private set; }

    /// <summary>Where the last sound was placed. Tests read it.</summary>
    public MapCoordinates? LastAt { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFBetrayalSoundEvent>(OnSound);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Clear());
    }

    private void Clear()
    {
        _pending.Clear();
        _trail.Clear();
        _stepsUntil = TimeSpan.Zero;
    }

    private float Volume() => Math.Clamp(_config.GetCVar(WFBetrayalCVars.Sounds), 0f, 1f);

    private void OnSound(WFBetrayalSoundEvent ev)
    {
        if (Volume() <= 0f || _player.LocalEntity is not { } me)
            return;

        var here = _transform.GetMapCoordinates(me);
        if (here.MapId == MapId.Nullspace)
            return;

        var intensity = Math.Clamp(ev.Intensity, 0f, 1f);
        switch (ev.Kind)
        {
            case WFBetrayalSoundKind.Knock:
                Knock(here, intensity);
                break;
            case WFBetrayalSoundKind.Scrape:
                Scrape(here, intensity);
                break;
            case WFBetrayalSoundKind.Breath:
                Breath(me, here, intensity);
                break;
            case WFBetrayalSoundKind.Footsteps:
                _stepsUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Clamp(ev.Seconds, 2f, 40f));
                _stepIntensity = intensity;
                _stopped = 0f;
                _trail.Clear();
                break;
        }
    }

    private void Schedule(double delay, SoundSpecifier sound, MapCoordinates where, float volume)
    {
        _pending.Add(new Pending(_timing.CurTime + TimeSpan.FromSeconds(delay), sound, where, volume + SharedAudioSystem.GainToVolume(Math.Max(Volume(), 0.01f))));
        LastAt = where;
    }

    /// <summary>Two to four knocks from just beyond a shut door a little way off.</summary>
    private bool Knock(MapCoordinates here, float intensity)
    {
        _doors.Clear();
        _lookup.GetEntitiesInRange(here.MapId, here.Position, 9f, _doors);
        var options = new List<Entity<DoorComponent>>();
        foreach (var door in _doors)
        {
            if (door.Comp.State == DoorState.Closed && (_transform.GetWorldPosition(door) - here.Position).Length() >= 2.5f)
                options.Add(door);
        }

        if (options.Count == 0)
            return false;

        var pick = _random.Pick(options);
        var at = _transform.GetWorldPosition(pick);
        var away = at - here.Position;
        var beyond = at + (away.LengthSquared() > 0.01f ? Vector2.Normalize(away) * 0.7f : Vector2.Zero);
        var count = _random.Next(2, 5);
        var delay = 0.0;
        for (var i = 0; i < count; i++)
        {
            Schedule(delay, new SoundCollectionSpecifier(WFBetrayalSounds.Knocks), new MapCoordinates(beyond, here.MapId), -4f + 8f * intensity);
            delay += _random.NextFloat(0.35f, 0.7f);
        }

        Knocks++;
        return true;
    }

    /// <summary>A scrape from inside a wall a few tiles off, twice.</summary>
    private bool Scrape(MapCoordinates here, float intensity)
    {
        for (var attempt = 0; attempt < 14; attempt++)
        {
            var spot = new MapCoordinates(here.Position + _random.NextAngle().ToVec() * _random.NextFloat(3f, 8f), here.MapId);
            if (!_maps.TryFindGridAt(spot, out var grid, out var gridComp))
                continue;

            var tile = _maps.GetTileRef(grid, gridComp, spot);
            if (_turf.IsSpace(tile) || !_turf.IsTileBlocked(tile, CollisionGroup.Impassable))
                continue;

            var centre = _transform.ToMapCoordinates(_turf.GetTileCenter(tile));
            Schedule(0, new SoundCollectionSpecifier(WFBetrayalSounds.Scrapes), centre, -6f + 7f * intensity);
            Schedule(_random.NextFloat(0.9f, 1.8f), new SoundCollectionSpecifier(WFBetrayalSounds.Scrapes), centre, -8f + 7f * intensity);
            Scrapes++;
            return true;
        }

        return false;
    }

    /// <summary>A breath at the player's back, close enough to feel.</summary>
    private void Breath(EntityUid me, MapCoordinates here, float intensity)
    {
        var facing = _transform.GetWorldRotation(me).GetDir().ToVec();
        var behind = new MapCoordinates(here.Position - facing * _random.NextFloat(0.7f, 1.1f), here.MapId);
        Schedule(0, new SoundCollectionSpecifier(WFBetrayalSounds.Breaths), behind, -9f + 7f * intensity);
        Breaths++;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.CurTime;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var pending = _pending[i];
            if (now < pending.At)
                continue;

            _pending.RemoveAt(i);
            if (Volume() > 0f)
                PlayAt(pending.Sound, pending.Where, pending.Volume);
        }

        Follow(now, frameTime);
    }

    private void PlayAt(SoundSpecifier sound, MapCoordinates where, float volume)
    {
        _audio.PlayStatic(sound, Filter.Local(), _transform.ToCoordinates(where), false, AudioParams.Default.WithVolume(volume).WithVariation(0.05f));
    }

    /// <summary>
    /// Steps that trail the player along the path they just walked, a stride after each of theirs, and stop the moment the
    /// player stops. Given up on after a moment of standing still.
    /// </summary>
    private void Follow(TimeSpan now, float frameTime)
    {
        if (now >= _stepsUntil || _player.LocalEntity is not { } me || Volume() <= 0f)
        {
            _trail.Clear();
            return;
        }

        var position = _transform.GetWorldPosition(me);
        var map = _transform.GetMapCoordinates(me).MapId;
        var speed = _trail.Count > 0 && frameTime > 0f ? (position - _trail[^1].Position).Length() / frameTime : 0f;
        _trail.Add((now, position));
        _trail.RemoveAll(sample => (now - sample.At).TotalSeconds > HistorySeconds);

        if (speed < StopSpeed)
        {
            _stopped += frameTime;
            if (_stopped > GiveUpAfter)
                _stepsUntil = TimeSpan.Zero;

            return;
        }

        _stopped = 0f;
        if (now < _nextStep)
            return;

        // The place the player stood a moment ago: the walker behind them is always on their own trail.
        var behind = position;
        foreach (var sample in _trail)
        {
            if ((now - sample.At).TotalSeconds <= Lag)
            {
                behind = sample.Position;
                break;
            }
        }

        if ((behind - position).LengthSquared() < 1f)
            return;

        _nextStep = now + TimeSpan.FromSeconds(Math.Clamp(1.4f / Math.Max(speed, 0.5f), 0.28f, 0.6f));
        var heavy = _stepIntensity > 0.8f && _random.Prob(0.4f);
        PlayAt(new SoundCollectionSpecifier(heavy ? WFBetrayalSounds.HeavySteps : WFBetrayalSounds.Steps), new MapCoordinates(behind, map),
            -9f + 6f * _stepIntensity + SharedAudioSystem.GainToVolume(Math.Max(Volume(), 0.01f)));
        Steps++;
        LastAt = new MapCoordinates(behind, map);
    }
}
