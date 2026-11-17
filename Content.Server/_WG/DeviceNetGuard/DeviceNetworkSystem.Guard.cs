using Stopwatch = System.Diagnostics.Stopwatch;
using System.Linq;
using Content.Shared._WG.DeviceNetGuard;
using Content.Shared.DeviceNetwork.Events;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server.DeviceNetwork.Systems;

public sealed partial class DeviceNetworkSystem
{
    [Dependency] private IConfigurationManager _guardCfg = default!;
    [Dependency] private IGameTiming _guardTiming = default!;

    /// <summary>Senders named in a storm report.</summary>
    private const int StormTopSenders = 5;

    /// <summary>A storm that keeps coming back is reported in full at most this often.</summary>
    private static readonly TimeSpan StormReportInterval = TimeSpan.FromMinutes(1);

    private readonly Stopwatch _guardWatch = new();
    private TimeSpan _nextStormReport;
    private int _lastBacklog;
    private int _quietStorms;
    private long _quietShed;

    /// <summary>Packets waiting to be sent, in this tick's queue and the next.</summary>
    public int Backlog => _queueA.Count + _queueB.Count;

    /// <summary>Packets dropped by the guard since the server started.</summary>
    public long ShedPackets { get; private set; }

    /// <summary>
    ///     Sends this tick's packets within a time budget and drops a runaway backlog. Returns false when the
    ///     guard is off and the upstream loop should run.
    /// </summary>
    private bool GuardedUpdate()
    {
        if (!_guardCfg.GetCVar(DeviceNetGuardCVars.Enabled))
            return false;

        var backlog = Backlog;
        var growing = backlog > _lastBacklog;
        _lastBacklog = backlog;

        // A large backlog that is shrinking is a burst the budget is already working through.
        if (growing && backlog > _guardCfg.GetCVar(DeviceNetGuardCVars.MaxBacklog))
        {
            // Devices feeding each other more packets than they consume never settle; start them from nothing.
            _lastBacklog = 0;
            ShedPackets += backlog;
            ReportStorm(backlog);
            _queueA.Clear();
            _queueB.Clear();
            return true;
        }

        var budget = TimeSpan.FromMilliseconds(_guardCfg.GetCVar(DeviceNetGuardCVars.TickBudgetMs));
        // At least one, or a zero budget would never send anything.
        var minPackets = Math.Max(1, _guardCfg.GetCVar(DeviceNetGuardCVars.MinPacketsPerTick));
        var sent = 0;
        _guardWatch.Restart();

        while (_activeQueue.Count > 0)
        {
            // The rest stays at the head of the active queue; newer packets keep collecting behind it.
            if (sent >= minPackets && _guardWatch.Elapsed >= budget)
                return true;

            SendPacket(_activeQueue.Dequeue());
            sent++;
        }

        SwapQueues();
        return true;
    }

    /// <summary>
    ///     Logs a dropped backlog with the devices sending most of it and their grids. Storms within
    ///     <see cref="StormReportInterval"/> of a report are only counted, and summed into the next one.
    /// </summary>
    private void ReportStorm(int backlog)
    {
        var now = _guardTiming.RealTime;
        if (now < _nextStormReport)
        {
            _quietStorms++;
            _quietShed += backlog;
            return;
        }

        _nextStormReport = now + StormReportInterval;

        var counts = new Dictionary<EntityUid, int>();
        CountSenders(_queueA, counts);
        CountSenders(_queueB, counts);

        var top = counts
            .OrderByDescending(pair => pair.Value)
            .Take(StormTopSenders)
            .Select(pair => $"{pair.Value}x {DescribeSender(pair.Key)}");

        var since = _quietStorms > 0
            ? $" {_quietStorms} more storms dropped {_quietShed} packets since the last report."
            : string.Empty;
        _quietStorms = 0;
        _quietShed = 0;

        Log.Error($"Device network packet storm: dropped {backlog} queued packets from {counts.Count} devices.{since} " +
                  $"Top senders: {string.Join("; ", top)}");
    }

    private static void CountSenders(Queue<DeviceNetworkPacketEvent> queue, Dictionary<EntityUid, int> counts)
    {
        foreach (var packet in queue)
        {
            counts[packet.Sender] = counts.GetValueOrDefault(packet.Sender) + 1;
        }
    }

    /// <summary>A sender with its prototype and the grid it is on, for the storm report.</summary>
    private string DescribeSender(EntityUid sender)
    {
        if (TerminatingOrDeleted(sender))
            return $"{sender} (deleted)";

        var grid = Transform(sender).GridUid;
        return grid is { } gridUid
            ? $"{ToPrettyString(sender)} on {ToPrettyString(gridUid)}"
            : $"{ToPrettyString(sender)} off-grid";
    }
}
