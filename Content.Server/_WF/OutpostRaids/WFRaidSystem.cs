using Content.Server._WF.Outposts.Components;
using Content.Server._WF.ShipPa;
using Content.Server.Administration.Logs;
using Content.Server.Power.EntitySystems;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.OutpostRaids;

/// <summary>
/// Raids on their way to outposts: warns each outpost ahead of its raid, by its early warning mast or, without one,
/// moments before, and raises <see cref="WFRaidArrivedEvent"/> when the raid lands.
/// </summary>
public sealed class WFRaidSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ShipAlertSystem _alert = default!;

    /// <summary>The ship code an early warning sets.</summary>
    public const string AlertCode = "WFShipCodeRed";

    /// <summary>How long before a raid an outpost without a mast still notices it coming.</summary>
    public static readonly TimeSpan LateWarning = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    private readonly List<WFPendingRaid> _raids = new();
    private int _nextId;
    private TimeSpan _nextUpdate;

    /// <summary>The raids on their way.</summary>
    public IReadOnlyList<WFPendingRaid> Raids => _raids;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _raids.Clear());
    }

    /// <summary>Sends a raid to an outpost, landing at <paramref name="arrival"/>; returns its id.</summary>
    public int ScheduleRaid(EntityUid outpost, TimeSpan arrival)
    {
        var raid = new WFPendingRaid(++_nextId, outpost, arrival);
        _raids.Add(raid);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"A raid on {ToPrettyString(outpost)} lands in {(arrival - _timing.CurTime).TotalSeconds:F0} s");
        return raid.Id;
    }

    /// <summary>Calls off a raid before it lands.</summary>
    public bool CancelRaid(int id)
    {
        return _raids.RemoveAll(raid => raid.Id == id) > 0;
    }

    /// <summary>The longest warning a powered mast on an outpost gives, or null without one.</summary>
    public TimeSpan? WarningLead(EntityUid outpost)
    {
        TimeSpan? lead = null;
        var masts = EntityQueryEnumerator<WFEarlyWarningComponent, TransformComponent>();
        while (masts.MoveNext(out var uid, out var mast, out var xform))
        {
            if (xform.GridUid != outpost || !xform.Anchored || !_power.IsPowered(uid))
                continue;
            if (lead == null || mast.Lead > lead)
                lead = mast.Lead;
        }

        return lead;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;
        _nextUpdate = now + UpdateInterval;

        for (var i = _raids.Count - 1; i >= 0; i--)
        {
            var raid = _raids[i];
            if (!HasComp<WFOutpostComponent>(raid.Outpost))
            {
                _raids.RemoveAt(i);
                continue;
            }

            var left = raid.Arrival - now;
            if (left <= TimeSpan.Zero)
            {
                _raids.RemoveAt(i);
                var ev = new WFRaidArrivedEvent(raid.Id, raid.Outpost);
                RaiseLocalEvent(ref ev);
                continue;
            }

            if (!raid.Warned && WarningLead(raid.Outpost) is { } lead && left <= lead)
            {
                raid.Warned = true;
                _alert.SetCode(raid.Outpost, AlertCode);
                Notify(raid.Outpost, Loc.GetString("wf-raid-warning", ("seconds", (int) Math.Ceiling(left.TotalSeconds))));
            }
            else if (!raid.Warned && !raid.LateWarned && left <= LateWarning)
            {
                raid.LateWarned = true;
                Notify(raid.Outpost, Loc.GetString("wf-raid-late-warning"));
            }
        }
    }

    /// <summary>Pops a warning up for everyone on an outpost.</summary>
    public void Notify(EntityUid outpost, string message)
    {
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == outpost)
                _popup.PopupEntity(message, uid, uid, PopupType.LargeCaution);
        }
    }
}

/// <summary>A raid on its way to an outpost.</summary>
public sealed class WFPendingRaid(int id, EntityUid outpost, TimeSpan arrival)
{
    public readonly int Id = id;
    public readonly EntityUid Outpost = outpost;
    public readonly TimeSpan Arrival = arrival;

    /// <summary>A mast has sounded the alarm.</summary>
    public bool Warned;

    /// <summary>The outpost noticed it moments before, without a mast.</summary>
    public bool LateWarned;
}

/// <summary>Raised when a raid lands on an outpost; the raid director spawns it.</summary>
[ByRefEvent]
public readonly record struct WFRaidArrivedEvent(int Id, EntityUid Outpost);
