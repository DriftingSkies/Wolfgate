using Content.Server.Atmos.Monitor.Components;
using Content.Server.Power.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Consoles;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Pinpointer;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.Monitor.Systems;

public sealed partial class AtmosAlertsComputerSystem
{
    private readonly Dictionary<EntityUid, List<(NetEntity Device, AtmosAlarmType State)>> _wfAlarmBuckets = new();
    private bool _wfAlarmBucketsBuilt;

    /// <summary>Forgets the alarm buckets so the next pass rebuilds them.</summary>
    private void WFResetAlarmBuckets()
    {
        foreach (var list in _wfAlarmBuckets.Values)
        {
            list.Clear();
        }

        _wfAlarmBuckets.Clear();
        _wfAlarmBucketsBuilt = false;
    }

    /// <summary>Highest non-silenced air or fire alarm state on a grid, from one shared sweep of all alarms; used by consoles whose UI is closed.</summary>
    private AtmosAlarmType WFHighestAlert(EntityUid gridUid, AtmosAlertsComputerComponent console)
    {
        if (!_wfAlarmBucketsBuilt)
        {
            _wfAlarmBucketsBuilt = true;
            var query = AllEntityQuery<AtmosAlertsDeviceComponent, AtmosAlarmableComponent, TransformComponent>();
            while (query.MoveNext(out var ent, out var device, out var alarmable, out var xform))
            {
                if (xform.GridUid is not { } grid || !xform.Anchored)
                    continue;

                if (device.Group != AtmosAlertsComputerGroup.AirAlarm && device.Group != AtmosAlertsComputerGroup.FireAlarm)
                    continue;

                var state = alarmable.LastAlarmState == AtmosAlarmType.Emagged ? AtmosAlarmType.Normal : alarmable.LastAlarmState;
                if (TryComp<ApcPowerReceiverComponent>(ent, out var power) && !power.Powered)
                    state = AtmosAlarmType.Invalid;

                if (!_wfAlarmBuckets.TryGetValue(grid, out var list))
                {
                    list = new List<(NetEntity, AtmosAlarmType)>();
                    _wfAlarmBuckets[grid] = list;
                }

                list.Add((GetNetEntity(ent), state));
            }
        }

        var highest = AtmosAlarmType.Invalid;
        if (!_wfAlarmBuckets.TryGetValue(gridUid, out var entries))
            return highest;

        if (!HasComp<MapGridComponent>(gridUid) || !HasComp<NavMapComponent>(gridUid))
            return highest;

        foreach (var (device, state) in entries)
        {
            if (state > highest && !console.SilencedDevices.Contains(device))
                highest = state;
        }

        return highest;
    }
}
