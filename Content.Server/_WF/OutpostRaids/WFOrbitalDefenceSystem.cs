using Content.Server.Power.EntitySystems;
using Content.Shared._WF.OutpostRaids;
using Content.Shared.GameTicking;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.OutpostRaids;

/// <summary>
/// Orbital defence: a shuttle raid on an outpost with a powered sensor uplink or descent jammer descends from orbit
/// instead of landing at once. It spends <see cref="Salvos"/> salvos coming down, two more under a jammer; at each,
/// every powered battery on the outpost fires on it while an uplink tracks it. Shot down, it never lands. Only raid
/// shuttles are ever fired on.
/// </summary>
public sealed class WFOrbitalDefenceSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WFRaidDirectorSystem _director = default!;
    [Dependency] private WFRaidSystem _raids = default!;

    /// <summary>Salvos a descent takes without a jammer.</summary>
    public const int Salvos = 3;

    /// <summary>A raid shuttle's hull while it descends.</summary>
    public const float Hull = 100f;

    private readonly List<WFDescent> _descents = new();

    /// <summary>The shuttle raids descending now.</summary>
    public IReadOnlyList<WFDescent> Descents => _descents;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _descents.Clear());
    }

    /// <summary>Starts a raid shuttle's descent on an outpost that has orbital defence; false when it has none.</summary>
    public bool TryStartDescent(int raid, EntityUid outpost)
    {
        var tracked = HasPowered<WFSensorUplinkComponent>(outpost);
        var jammed = 0;
        var jammers = EntityQueryEnumerator<WFDescentJammerComponent, TransformComponent>();
        while (jammers.MoveNext(out var uid, out var jammer, out var xform))
        {
            if (xform.GridUid == outpost && xform.Anchored && _power.IsPowered(uid))
                jammed = Math.Max(jammed, jammer.ExtraSalvos);
        }

        if (!tracked && jammed == 0)
            return false;

        _descents.Add(new WFDescent(raid, outpost)
        {
            SalvosLeft = Salvos + jammed,
            NextSalvo = _timing.CurTime + Interval,
        });
        _raids.Notify(outpost, Loc.GetString(tracked ? "wf-orbital-descent-tracked" : "wf-orbital-descent-jammed"));
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        for (var i = _descents.Count - 1; i >= 0; i--)
        {
            var descent = _descents[i];
            if (now < descent.NextSalvo)
                continue;

            if (TerminatingOrDeleted(descent.Outpost))
            {
                _descents.RemoveAt(i);
                _director.FinishDescent(descent.Raid, false);
                continue;
            }

            descent.NextSalvo = now + Interval;
            descent.SalvosLeft--;
            Fire(descent);
            if (descent.Hull > 0 && descent.SalvosLeft > 0)
                continue;

            _descents.RemoveAt(i);
            var shotDown = descent.Hull <= 0;
            _raids.Notify(descent.Outpost, Loc.GetString(shotDown ? "wf-orbital-shot-down" : "wf-orbital-landing"));
            _director.FinishDescent(descent.Raid, shotDown);
        }
    }

    /// <summary>Every powered battery on the outpost fires once at the descending shuttle, if an uplink tracks it.</summary>
    private void Fire(WFDescent descent)
    {
        if (!HasPowered<WFSensorUplinkComponent>(descent.Outpost))
            return;

        var damage = 0f;
        var batteries = EntityQueryEnumerator<WFOrbitalBatteryComponent, TransformComponent>();
        while (batteries.MoveNext(out var uid, out var battery, out var xform))
        {
            if (xform.GridUid != descent.Outpost || !xform.Anchored || !_power.IsPowered(uid))
                continue;

            damage += battery.Damage;
            if (battery.Sound != null)
                _audio.PlayPvs(battery.Sound, uid);
        }

        if (damage <= 0)
            return;

        descent.Hull -= damage;
        if (descent.Hull > 0)
            _raids.Notify(descent.Outpost, Loc.GetString("wf-orbital-hit", ("hull", (int) Math.Ceiling(descent.Hull / Hull * 100))));
    }

    private bool HasPowered<T>(EntityUid outpost) where T : IComponent
    {
        var query = EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == outpost && xform.Anchored && _power.IsPowered(uid))
                return true;
        }

        return false;
    }

    private TimeSpan Interval => TimeSpan.FromSeconds(_cfg.GetCVar(OutpostRaidCVars.SalvoInterval));
}

/// <summary>A raid shuttle on its way down to a defended outpost.</summary>
public sealed class WFDescent(int raid, EntityUid outpost)
{
    public readonly int Raid = raid;
    public readonly EntityUid Outpost = outpost;
    public float Hull = WFOrbitalDefenceSystem.Hull;
    public int SalvosLeft;
    public TimeSpan NextSalvo;
}
