using System.Numerics;
using Content.Server._WF.Warheads.Components;
using Content.Server.Administration.Logs;
using Content.Server.Atmos.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Nuke;
using Content.Server.Popups;
using Content.Shared._WF.Warheads;
using Content.Shared.Database;
using Content.Shared.Emp;
using Content.Shared.Nuke;
using Content.Shared.Radiation.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._WF.Warheads;

/// <summary>
/// Runs warhead countdowns and detonations: the blast, radiation burst, fallout, EMP and the client effects.
/// </summary>
public sealed partial class WFWarheadSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedEmpSystem _emp = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private TransformSystem _transform = default!;

    public static readonly SoundSpecifier CountdownVoice = new SoundPathSpecifier("/Audio/_WF/Warheads/delta_countdown.ogg");
    public static readonly SoundSpecifier FinalAlarm = new SoundPathSpecifier("/Audio/_WF/Warheads/alarm.ogg");
    public static readonly SoundSpecifier ArmSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");
    public static readonly SoundSpecifier DisarmSound = new SoundPathSpecifier("/Audio/Misc/notice2.ogg");
    public static readonly EntProtoId PromptRadiationProto = "WFNuclearPromptRadiation";

    /// <summary>Seconds before zero the station nuke's voice countdown starts, matching its final phase.</summary>
    private const float NukeFinalPhase = 10f;

    private float _falloutAccumulator;

    public override void Initialize()
    {
        base.Initialize();
        InitializeArming();
        InitializeUi();
        SubscribeLocalEvent<WFWarheadComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WFWarheadComponent> ent, ref MapInitEvent args)
    {
        UpdateAppearance(ent);
    }

    /// <summary>The payload a warhead sets off: its custom one, else its preset.</summary>
    public WFWarheadPayload? GetPayload(WFWarheadComponent comp)
    {
        if (comp.CustomPayload != null)
            return comp.CustomPayload;

        return comp.Payload is { } id && _proto.TryIndex(id, out var proto) ? proto.Payload : null;
    }

    /// <summary>Starts the countdown, from the warhead's timer unless one is given.</summary>
    public void StartCountdown(Entity<WFWarheadComponent> ent, EntityUid? user, float? seconds = null)
    {
        var time = Math.Max(seconds ?? ent.Comp.Timer, 0f);
        ent.Comp.State = WFWarheadState.Countdown;
        ent.Comp.DetonateAt = _timing.CurTime + TimeSpan.FromSeconds(time);
        ent.Comp.FinalPhaseStarted = false;
        Dirty(ent);

        SetDisplay(ent, ent.Comp.DetonateAt);
        UpdateAppearance(ent);
        _audio.PlayPvs(ArmSound, ent);
        var armed = new WFWarheadArmedEvent(ent, user);
        RaiseLocalEvent(ref armed);

        _adminLog.Add(LogType.Explosion, LogImpact.Extreme,
            $"{(user != null ? ToPrettyString(user.Value) : "Unknown")} started {ToPrettyString(ent):warhead} counting down from {time}s at {_transform.GetMapCoordinates(ent)}");
    }

    /// <summary>Stops a countdown, or puts the safety back on.</summary>
    public void Disarm(Entity<WFWarheadComponent> ent, EntityUid? user)
    {
        if (ent.Comp.State == WFWarheadState.Final)
            return;

        ent.Comp.State = WFWarheadState.Safe;
        ent.Comp.FinalPhaseStarted = false;
        Dirty(ent);

        SetDisplay(ent, null);
        UpdateAppearance(ent);
        _audio.PlayPvs(DisarmSound, ent);

        _adminLog.Add(LogType.Explosion, LogImpact.High,
            $"{(user != null ? ToPrettyString(user.Value) : "Unknown")} disarmed {ToPrettyString(ent):warhead}");
    }

    public void SetSafety(Entity<WFWarheadComponent> ent, bool safe, EntityUid? user)
    {
        if (safe)
        {
            Disarm(ent, user);
            return;
        }

        ent.Comp.State = WFWarheadState.Armed;
        Dirty(ent);
        UpdateAppearance(ent);
        var armed = new WFWarheadArmedEvent(ent, user);
        RaiseLocalEvent(ref armed);
        _adminLog.Add(LogType.Explosion, LogImpact.High,
            $"{(user != null ? ToPrettyString(user.Value) : "Unknown")} took the safety off {ToPrettyString(ent):warhead}");
    }

    private void UpdateAppearance(Entity<WFWarheadComponent> ent)
    {
        _appearance.SetData(ent, WFWarheadVisuals.State, ent.Comp.State);
        UpdateUi(ent, ent.Comp);
    }

    private void SetDisplay(EntityUid uid, TimeSpan? end)
    {
        var display = EnsureComp<WFCountdownDisplayComponent>(uid);
        if (display.End == end)
            return;

        display.End = end;
        Dirty(uid, display);
    }

    /// <summary>The voice countdown and alarm of a warhead's final seconds.</summary>
    private void PlayFinalPhase(EntityUid uid, bool mapWide)
    {
        var filter = mapWide
            ? Filter.BroadcastMap(Transform(uid).MapID)
            : Filter.Pvs(uid, 4f);

        _audio.PlayGlobal(FinalAlarm, filter, true);
        _audio.PlayGlobal(CountdownVoice, filter, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFWarheadComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.State is not (WFWarheadState.Countdown or WFWarheadState.Final))
                continue;

            var remaining = (float) (comp.DetonateAt - now).TotalSeconds;
            var payload = GetPayload(comp);

            if (!comp.FinalPhaseStarted && remaining <= comp.FinalPhase)
            {
                comp.FinalPhaseStarted = true;
                comp.State = WFWarheadState.Final;
                Dirty(uid, comp);
                UpdateAppearance((uid, comp));
                PlayFinalPhase(uid, payload?.MapWideAudio ?? true);
            }

            if (remaining > 0f)
                continue;

            if (payload == null)
            {
                Log.Error($"{ToPrettyString(uid)} reached zero with no payload");
                QueueDel(uid);
                continue;
            }

            Detonate(uid, payload, null);
        }

        UpdateNukes(now);
        UpdateDeadMan();

        _falloutAccumulator += frameTime;
        if (_falloutAccumulator >= 1f)
        {
            _falloutAccumulator = 0f;
            UpdateFallout(now);
        }
    }

    /// <summary>Mirrors armed station nukes onto the countdown display and voices their last seconds.</summary>
    private void UpdateNukes(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFNukePayloadComponent, NukeComponent>();
        while (query.MoveNext(out var uid, out var wf, out var nuke))
        {
            if (nuke.Status != NukeStatus.ARMED)
            {
                wf.PlayedVoice = false;
                SetDisplay(uid, null);
                continue;
            }

            var end = now + TimeSpan.FromSeconds(nuke.RemainingTime);
            var display = EnsureComp<WFCountdownDisplayComponent>(uid);
            if (display.End is not { } shown || Math.Abs((shown - end).TotalSeconds) > 0.5)
                SetDisplay(uid, end);

            if (wf.PlayedVoice || nuke.RemainingTime > NukeFinalPhase)
                continue;

            wf.PlayedVoice = true;
            var mapWide = _proto.TryIndex(wf.Payload, out var proto) && proto.Payload.MapWideAudio;
            var filter = mapWide ? Filter.BroadcastMap(Transform(uid).MapID) : Filter.Pvs(uid, 4f);
            _audio.PlayGlobal(CountdownVoice, filter, true);
        }
    }

    private void UpdateFallout(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFFalloutComponent, RadiationSourceComponent>();
        while (query.MoveNext(out var uid, out var fallout, out var source))
        {
            var fraction = fallout.Fraction(now);
            if (fraction < fallout.Cutoff)
            {
                QueueDel(uid);
                continue;
            }

            source.Intensity = fallout.Intensity * fraction;
        }
    }

    /// <summary>
    /// Called by the station nuke as it goes off. Adds the warhead effects; the nuke runs its own blast.
    /// </summary>
    public void OnNukeDetonated(EntityUid nuke)
    {
        if (!TryComp<WFNukePayloadComponent>(nuke, out var wf) || !_proto.TryIndex(wf.Payload, out var proto))
            return;

        Detonate(nuke, proto.Payload, null, deleteSource: false);
    }

    /// <summary>Sets off a payload where an entity is, then deletes it.</summary>
    public void Detonate(EntityUid source, WFWarheadPayload payload, EntityUid? cause, bool deleteSource = true)
    {
        var xform = Transform(source);
        var coords = _transform.GetMapCoordinates(source, xform);
        var grid = xform.GridUid;

        _adminLog.Add(LogType.Explosion, LogImpact.Extreme,
            $"{ToPrettyString(source):warhead} detonated at {coords}");

        if (deleteSource && !payload.DeleteGrid)
            QueueDel(source);

        Detonate(coords, grid, payload, cause);

        // A deleted grid took the warhead with it.
        if (deleteSource && payload.DeleteGrid && !TerminatingOrDeleted(source))
            QueueDel(source);
    }

    /// <summary>Sets off a payload at a point. <paramref name="grid"/> is the grid it sits on, if any.</summary>
    public void Detonate(MapCoordinates coords, EntityUid? grid, WFWarheadPayload payload, EntityUid? cause)
    {
        if (coords.MapId == MapId.Nullspace || !_map.TryGetMap(coords.MapId, out var mapUid))
            return;

        // Before the grid goes or anything is erased, so what only a nuke can kill sees the blast.
        var detonated = new WFNuclearDetonatedEvent(coords, payload.EraseRadius * 2f + 16f, cause);
        RaiseLocalEvent(ref detonated);

        var planet = HasComp<MapAtmosphereComponent>(mapUid.Value);

        RaiseNetworkEvent(new WFNuclearDetonationEvent
        {
            Map = GetNetEntity(mapUid.Value),
            Position = coords.Position,
            Start = _timing.CurTime,
            FlashRange = payload.FlashRange,
            FireballRadius = payload.FireballRadius,
            ShockwaveRange = payload.ShockwaveRange,
            ShockwaveSpeed = payload.ShockwaveSpeed,
            ShakeStrength = payload.ShakeStrength,
            Duration = payload.EffectDuration,
            Mushroom = planet,
        }, Filter.BroadcastMap(coords.MapId));

        if (payload.DetonationSound is { } sound)
            _audio.PlayGlobal(new SoundPathSpecifier(sound), Filter.BroadcastMap(coords.MapId), true);

        // Deleting the grid first lets the blast run through the space it leaves and hit the grids around it.
        if (payload.DeleteGrid
            && grid is { } gridUid
            && gridUid != mapUid
            && HasComp<MapGridComponent>(gridUid)
            && !HasComp<MapComponent>(gridUid))
        {
            _adminLog.Add(LogType.Explosion, LogImpact.Extreme, $"Warhead deleted grid {ToPrettyString(gridUid)}");
            EntityManager.System<Content.Server._WF.Performance.WFGridDeleteSystem>().QueueDel(gridUid);
        }

        Erase(coords, payload.EraseRadius);

        if (payload.ExplosionType is { } type && payload.TotalIntensity > 0f)
        {
            _explosion.QueueExplosion(coords, type, payload.TotalIntensity, payload.IntensitySlope,
                payload.MaxIntensity, cause, payload.TileBreakScale);
        }

        if (payload.PromptRadiation > 0f)
        {
            var burst = Spawn(PromptRadiationProto, coords);
            var radiation = EnsureComp<RadiationSourceComponent>(burst);
            radiation.Intensity = payload.PromptRadiation;
            radiation.Slope = payload.PromptRadiationSlope;
            EnsureComp<TimedDespawnComponent>(burst).Lifetime = payload.PromptRadiationDuration;
        }

        if (payload.FalloutIntensity > 0f)
        {
            SpawnFallout(coords.Position, coords, payload);

            for (var i = 0; i < payload.FalloutCount; i++)
            {
                var offset = _random.NextVector2(payload.FalloutSpread);
                SpawnFallout(coords.Position + offset, coords, payload);
            }
        }

        if (payload.EmpRange > 0f)
            _emp.EmpPulse(coords, payload.EmpRange, payload.EmpEnergy, TimeSpan.FromSeconds(payload.EmpDuration));
    }

    private void SpawnFallout(Vector2 position, MapCoordinates origin, WFWarheadPayload payload)
    {
        var uid = Spawn(payload.FalloutPrototype, new MapCoordinates(position, origin.MapId));
        var fallout = EnsureComp<WFFalloutComponent>(uid);
        fallout.Intensity = payload.FalloutIntensity;
        fallout.HalfLife = payload.FalloutHalfLife;
        fallout.Radius = payload.FalloutRadius;
        fallout.Start = _timing.CurTime;
        Dirty(uid, fallout);

        var radiation = EnsureComp<RadiationSourceComponent>(uid);
        radiation.Intensity = payload.FalloutIntensity;
        radiation.Slope = 1f / Math.Max(payload.FalloutRadius, 0.5f);
    }
}

/// <summary>Raised once for every nuclear detonation, before it does anything: where, and how far it kills outright.</summary>
[ByRefEvent]
public readonly record struct WFNuclearDetonatedEvent(MapCoordinates Coordinates, float KillRadius, EntityUid? Cause);

/// <summary>Raised once whenever a warhead starts counting down or has its safety taken off.</summary>
[ByRefEvent]
public readonly record struct WFWarheadArmedEvent(EntityUid Warhead, EntityUid? User);
