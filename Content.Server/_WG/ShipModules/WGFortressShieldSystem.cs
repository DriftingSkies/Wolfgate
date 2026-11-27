using Content.Server._Crescent.ShipShields;
using Content.Shared._WG.ShipModules;
using Content.Shared._WG.ShipShields;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// Runs the fortress shield: while it is on, the ship's raised field is hardened
/// (<see cref="WGShipShieldHardenedComponent"/>), its guns are silent and it builds hard heat. It switches off once the
/// field is down. Its hum fades in under the switch-on sound and loops until it is off.
/// </summary>
public sealed partial class WGFortressShieldSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ShipShieldsSystem _shields = default!;
    [Dependency] private WGShipModuleSystem _modules = default!;

    /// <summary>Gain the hum starts from, near silent.</summary>
    private const float QuietGain = 0.001f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipModuleLockEvent>(OnLock);
        SubscribeLocalEvent<WGShipModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<WGShipModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<WGFortressShieldActiveComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnLock(ref WGShipModuleLockEvent args)
    {
        if (args.Lock == WGShipModuleLock.None && args.Module.Effect is WGFortressShieldEffect
            && !_shields.IsWolfgateShipFieldRaised(args.Grid))
            args.Lock = WGShipModuleLock.NoField;
    }

    private void OnActivated(ref WGShipModuleActivatedEvent args)
    {
        if (args.Module.Effect is not WGFortressShieldEffect fortress)
            return;

        var hardened = EnsureComp<WGShipShieldHardenedComponent>(args.Grid);
        hardened.HitFactor = fortress.HitFactor;
        Dirty(args.Grid, hardened);
        var active = EnsureComp<WGFortressShieldActiveComponent>(args.Grid);
        active.HeatPerSecond = fortress.HeatPerSecond;
        StartHum(args.Grid, active, fortress, args.User);
    }

    /// <summary>Stops the hum with the switch, or when the hull is gone.</summary>
    private void OnShutdown(Entity<WGFortressShieldActiveComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Hum = _audio.Stop(ent.Comp.Hum);
    }

    /// <summary>Starts the hum loop near silent; <see cref="Update"/> fades it in.</summary>
    private void StartHum(EntityUid grid, WGFortressShieldActiveComponent active, WGFortressShieldEffect fortress, EntityUid? user)
    {
        active.Hum = _audio.Stop(active.Hum);
        if (fortress.Hum is not { } hum)
            return;

        var audioParams = hum.Params.WithLoop(true);
        active.HumGain = SharedAudioSystem.VolumeToGain(audioParams.Volume);
        active.FadeStart = _timing.CurTime + fortress.HumDelay;
        active.FadeTime = (float) fortress.HumFade.TotalSeconds;
        active.Faded = false;
        active.Hum = _audio.PlayGlobal(hum, _modules.Crew(grid, user), true,
            audioParams.WithVolume(SharedAudioSystem.GainToVolume(QuietGain)))?.Entity;
    }

    private void OnDeactivated(ref WGShipModuleDeactivatedEvent args)
    {
        if (args.Module.Effect is not WGFortressShieldEffect)
            return;

        RemCompDeferred<WGShipShieldHardenedComponent>(args.Grid);
        RemCompDeferred<WGFortressShieldActiveComponent>(args.Grid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGFortressShieldActiveComponent>();
        while (query.MoveNext(out var grid, out var fortress))
        {
            FadeHum(fortress, now);

            // Past capacity the field overloads, which ends the fortress at the next sweep.
            var (_, capacity) = _shields.GetWolfgateShipHeat(grid);
            _shields.AddWolfgateShipHeat(grid, capacity * fortress.HeatPerSecond * frameTime, hard: true);
        }
    }

    /// <summary>Raises the hum's volume along its fade-in.</summary>
    private void FadeHum(WGFortressShieldActiveComponent fortress, TimeSpan now)
    {
        if (fortress.Faded || fortress.Hum is not { } hum || now < fortress.FadeStart)
            return;

        var progress = fortress.FadeTime > 0f ? (float) (now - fortress.FadeStart).TotalSeconds / fortress.FadeTime : 1f;
        fortress.Faded = progress >= 1f;
        _audio.SetGain(hum, MathHelper.Lerp(QuietGain, fortress.HumGain, Math.Clamp(progress, 0f, 1f)));
    }
}

/// <summary>A fortress shield switched on on a ship's grid.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGFortressShieldActiveComponent : Component
{
    /// <summary>Share of the ship's heat capacity added each second, as hard heat.</summary>
    [ViewVariables]
    public float HeatPerSecond;

    /// <summary>The hum loop playing to the crew.</summary>
    [ViewVariables]
    public EntityUid? Hum;

    /// <summary>Gain the hum fades up to.</summary>
    [ViewVariables]
    public float HumGain;

    /// <summary>When the hum starts to fade in.</summary>
    [ViewVariables]
    public TimeSpan FadeStart;

    /// <summary>Seconds the fade-in takes.</summary>
    [ViewVariables]
    public float FadeTime;

    /// <summary>Whether the hum has reached full volume.</summary>
    [ViewVariables]
    public bool Faded;
}
