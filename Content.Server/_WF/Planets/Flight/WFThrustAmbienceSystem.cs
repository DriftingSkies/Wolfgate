using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets.Flight;

/// <summary>Plays one engine loop per hull to its crew while any linear thruster is firing.</summary>
public sealed partial class WFThrustAmbienceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WFGridAudienceSystem _audience = default!;
    [Dependency] private WFThrusterIndexSystem _thrusterIndex = default!;

    /// <summary>Looping engine sound.</summary>
    public static readonly SoundSpecifier ThrustLoop = new SoundPathSpecifier("/Audio/_WF/Planets/Flight/thrust_loop.ogg");

    // dB: -6 + 20 * log10(0.6).
    private const float ThrustVolume = -10.44f;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(0.1);
    private static readonly TimeSpan RecutInterval = TimeSpan.FromSeconds(10);

    private TimeSpan _nextSweep;
    private readonly HashSet<EntityUid> _thrusting = new();
    private readonly List<(EntityUid Grid, WFThrustAmbienceComponent Comp)> _playing = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFThrustAmbienceComponent, ComponentShutdown>(OnAmbienceShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;
        _thrusting.Clear();

        // Walk each shuttle's indexed thrusters rather than every thruster in the world.
        var shuttles = EntityQueryEnumerator<ShuttleComponent>();
        while (shuttles.MoveNext(out var shuttleGrid, out _))
        {
            if (_thrusterIndex.ThrustersOn(shuttleGrid) is not { } onGrid)
                continue;

            foreach (var uid in onGrid)
            {
                if (IsFiring(uid))
                {
                    _thrusting.Add(shuttleGrid);
                    break;
                }
            }
        }

        // Thrusters torn off their hull still roar.
        var crashed = EntityQueryEnumerator<WFCrashThrustComponent, TransformComponent>();
        while (crashed.MoveNext(out var uid, out var crashThrust, out var xform))
        {
            if (crashThrust.Detached && xform.GridUid is { } grid && IsFiring(uid))
                _thrusting.Add(grid);
        }

        // Collected first because the loop below removes components.
        _playing.Clear();
        var playing = EntityQueryEnumerator<WFThrustAmbienceComponent>();
        while (playing.MoveNext(out var uid, out var comp))
        {
            _playing.Add((uid, comp));
        }

        foreach (var (grid, comp) in _playing)
        {
            if (_thrusting.Contains(grid))
                continue;

            comp.Stream = _audio.Stop(comp.Stream);
            RemComp<WFThrustAmbienceComponent>(grid);
        }

        foreach (var grid in _thrusting)
        {
            var comp = EnsureComp<WFThrustAmbienceComponent>(grid);

            if (comp.Stream != null && _timing.CurTime < comp.NextRecut)
                continue;

            comp.Stream = _audio.Stop(comp.Stream);
            comp.Stream = _audio.PlayGlobal(ThrustLoop, _audience.Aboard(grid), true, AudioParams.Default.WithLoop(true).WithVolume(ThrustVolume))?.Entity;
            comp.NextRecut = _timing.CurTime + RecutInterval;
        }
    }

    private bool IsFiring(EntityUid uid)
    {
        return TryComp<ThrusterComponent>(uid, out var thruster)
               && thruster.Type == ThrusterType.Linear && thruster.Enabled && thruster.IsOn && thruster.Firing;
    }

    private void OnAmbienceShutdown(EntityUid uid, WFThrustAmbienceComponent component, ComponentShutdown args)
    {
        component.Stream = _audio.Stop(component.Stream);
    }
}

/// <summary>Marks a hull whose thrust loop is playing, and holds the stream.</summary>
[RegisterComponent]
public sealed partial class WFThrustAmbienceComponent : Component
{
    [ViewVariables]
    public EntityUid? Stream;

    [ViewVariables]
    public TimeSpan NextRecut;
}
