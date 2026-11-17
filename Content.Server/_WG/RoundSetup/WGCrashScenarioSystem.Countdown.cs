using System.Linq;
using Content.Server._WG.Planets.Flight;
using Content.Shared._WG.RoundSetup;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._WG.RoundSetup;

public sealed partial class WGCrashScenarioSystem
{
    [Dependency] private IPlayerManager _countdownPlayers = default!;

    private readonly Dictionary<ICommonSession, (TimeSpan? Deadline, NetEntity? Body)> _countdowns = new();
    private TimeSpan _nextCountdownUpdate;

    private void InitializeCountdown()
    {
        SubscribeLocalEvent<PlayerAttachedEvent>(args => SendCountdown(args.Player, force: true));
        SubscribeLocalEvent<PlayerDetachedEvent>(args => SendCountdown(args.Player, force: true));
    }

    /// <summary>Returns the shared preparation deadline only for living occupants of a pending crash ship.</summary>
    public bool TryGetCountdown(EntityUid body, out TimeSpan deadline)
    {
        deadline = default;
        if (TerminatingOrDeleted(body)
            || HasComp<GhostComponent>(body)
            || !TryComp<MobStateComponent>(body, out var mob)
            || mob.CurrentState is not (MobState.Alive or MobState.Critical)
            || Transform(body).GridUid is not { } hull
            || TerminatingOrDeleted(hull)
            || !TryComp<WGCrashScenarioComponent>(hull, out var crash)
            || !crash.Started || crash.Descending || crash.Crashed
            || HasComp<WGCrashImpactComponent>(hull)
            || MetaData(hull).EntityPaused
            || crash.DescendAt <= _timing.CurTime)
            return false;

        deadline = crash.DescendAt;
        return true;
    }

    /// <summary>Checks boarding, disembarking and death without broadcasting timer ticks.</summary>
    private void UpdateCountdown()
    {
        if (_timing.CurTime < _nextCountdownUpdate)
            return;
        _nextCountdownUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);

        foreach (var session in _countdowns.Keys.ToArray())
        {
            if (session.Status != SessionStatus.InGame)
                _countdowns.Remove(session);
        }
        foreach (var session in _countdownPlayers.Sessions)
            SendCountdown(session);
    }

    /// <summary>Attachment and successful spawning refresh immediately, including on reconnection.</summary>
    private void SendCountdown(ICommonSession session, bool force = false)
    {
        if (session.Status != SessionStatus.InGame)
            return;

        TimeSpan? deadline = null;
        NetEntity? body = null;
        if (session.AttachedEntity is { } attached && TryGetCountdown(attached, out var at))
        {
            deadline = at;
            body = GetNetEntity(attached);
        }
        var state = (deadline, body);
        if (!force && _countdowns.TryGetValue(session, out var previous) && previous == state)
            return;
        _countdowns[session] = state;
        RaiseNetworkEvent(new WGCrashCountdownEvent(deadline, body), session);
    }

    private void ResetCountdown()
    {
        foreach (var session in _countdowns.Keys)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(new WGCrashCountdownEvent(null, null), session);
        }
        _countdowns.Clear();
        _nextCountdownUpdate = TimeSpan.Zero;
    }
}
