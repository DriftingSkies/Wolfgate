using System.Linq;
using Content.Shared._WF.MappingMode;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    /// <summary>Sessions due for an autosave, saved one per tick.</summary>
    private readonly Queue<int> _autosaveQueue = new();
    private readonly HashSet<int> _autosaveQueued = new();

    private void InitializeAutosave()
    {
        SubscribeNetworkEvent<WFMappingAutosaveStepRequest>(OnAutosaveStep);
    }

    /// <summary>The step a new session starts with: the one nearest <c>autosave_interval</c>.</summary>
    private int DefaultAutosaveMinutes()
    {
        var minutes = _cfg.GetCVar(WFMappingModeCVars.AutosaveInterval) / 60.0;
        return WFMappingAutosaveSteps.Minutes.Where(m => m > 0).MinBy(m => Math.Abs(m - minutes));
    }

    /// <summary>
    /// The time between a session's autosaves: its step clamped to <c>autosave_min_interval</c> ..
    /// <c>autosave_max_interval</c>; null when the owner turned autosave off.
    /// </summary>
    public TimeSpan? AutosaveInterval(WFMappingSession session)
    {
        if (session.AutosaveMinutes <= 0)
            return null;

        var min = Math.Max(1, _cfg.GetCVar(WFMappingModeCVars.AutosaveMinInterval));
        var max = Math.Max(min, _cfg.GetCVar(WFMappingModeCVars.AutosaveMaxInterval));
        return TimeSpan.FromSeconds(Math.Clamp(session.AutosaveMinutes * 60, min, max));
    }

    private void InitAutosave(WFMappingSession session)
    {
        session.AutosaveMinutes = DefaultAutosaveMinutes();
        ScheduleAutosave(session, offset: true);
    }

    /// <summary>
    /// Sets the next autosave check one interval away; with <paramref name="offset"/> up to half an interval later,
    /// so sessions opened together don't save together.
    /// </summary>
    private void ScheduleAutosave(WFMappingSession session, bool offset)
    {
        if (AutosaveInterval(session) is not { } interval)
        {
            session.NextAutosave = TimeSpan.Zero;
            return;
        }

        var delay = offset ? interval * (1 + _random.NextDouble() * 0.5) : interval;
        session.NextAutosave = _timing.CurTime + delay;
    }

    private void OnAutosaveStep(WFMappingAutosaveStepRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetPresent(player, out var session) ||
            !TryThrottle(_toolsThrottle, player, "autosave step"))
        {
            return;
        }

        if (session.Owner != player.UserId)
        {
            Refuse(player, "wf-mapping-mode-autosave-not-owner");
            return;
        }

        if (!WFMappingAutosaveSteps.IsStep(ev.Minutes) || ev.Minutes == session.AutosaveMinutes)
            return;

        session.AutosaveMinutes = ev.Minutes;
        ScheduleAutosave(session, offset: false);
        SendState(session);
    }

    /// <summary>
    /// Queues the sessions whose autosave is due and changed since their last save, and saves the first of the queue.
    /// </summary>
    private void UpdateAutosave(TimeSpan now)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.AutosaveMinutes <= 0 || now < session.NextAutosave || _autosaveQueued.Contains(session.Id))
                continue;

            ScheduleAutosave(session, offset: false);
            if (session.Dirty)
            {
                _autosaveQueue.Enqueue(session.Id);
                _autosaveQueued.Add(session.Id);
            }
            else
            {
                SendState(session);
            }
        }

        while (_autosaveQueue.TryDequeue(out var id))
        {
            _autosaveQueued.Remove(id);
            if (!_sessions.TryGetValue(id, out var session) || !session.Dirty)
                continue;

            _library.Autosave(session);
            SendState(session);
            break;
        }
    }

    private void ClearAutosaveQueue()
    {
        _autosaveQueue.Clear();
        _autosaveQueued.Clear();
    }
}
