using System.Linq;
using Content.Server.Atmos.Monitor.Components;
using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Database;
using Content.Shared.DeviceNetwork.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.MappingTools;

public sealed partial class MappingToolsSystem
{
    /// <summary>How far from a click a monitor or air alarm may be and still be picked, in tiles.</summary>
    private const float ThresholdPickRange = 0.45f;

    private void InitializeThresholds()
    {
        SubscribeNetworkEvent<MappingToolsThresholdsRequestEvent>(OnThresholdsRequest);
        SubscribeNetworkEvent<MappingToolsSetThresholdsEvent>(OnSetThresholds);
    }

    private void OnThresholdsRequest(MappingToolsThresholdsRequestEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            SendThresholds(args.SenderSession, ev);
    }

    private void OnSetThresholds(MappingToolsSetThresholdsEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            SetThresholds(args.SenderSession, ev);
    }

    private bool IsThresholdTarget(EntityUid uid)
    {
        return HasComp<AtmosMonitorComponent>(uid) || HasComp<AirAlarmComponent>(uid);
    }

    /// <summary>The clicked monitor or air alarm, else the nearest one in reach of the click.</summary>
    private EntityUid? PickThresholdTarget(MappingToolsThresholdsRequestEvent ev)
    {
        if (TryGetEntity(ev.Clicked, out var clicked) && !TerminatingOrDeleted(clicked) && IsThresholdTarget(clicked.Value))
            return clicked;

        var coords = GetCoordinates(ev.Coordinates);
        if (!coords.IsValid(EntityManager))
            return null;

        var position = _transform.ToMapCoordinates(coords);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var uid in _lookup.GetEntitiesInRange(position, ThresholdPickRange))
        {
            if (!IsThresholdTarget(uid))
                continue;

            var distance = (_transform.GetWorldPosition(uid) - position.Position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>The monitors a target stands for: itself, or the ones an air alarm lists.</summary>
    private IEnumerable<EntityUid> MonitorsOf(EntityUid target)
    {
        if (HasComp<AtmosMonitorComponent>(target))
        {
            yield return target;
            yield break;
        }

        if (!HasComp<AirAlarmComponent>(target) || !TryComp(target, out DeviceListComponent? list))
            yield break;

        foreach (var device in list.Devices)
        {
            if (!TerminatingOrDeleted(device) && HasComp<AtmosMonitorComponent>(device))
                yield return device;
        }
    }

    /// <summary>
    /// The thresholds a monitor checks: its own values, else its prototypes' (before map init they aren't copied in).
    /// </summary>
    public MappingThresholdSet EffectiveThresholds(AtmosMonitorComponent monitor)
    {
        var set = new MappingThresholdSet
        {
            Temperature = monitor.TemperatureThreshold ?? FromPrototype(monitor.TemperatureThresholdId),
            Pressure = monitor.PressureThreshold ?? FromPrototype(monitor.PressureThresholdId),
        };

        if (monitor.GasThresholds != null)
        {
            foreach (var (gas, threshold) in monitor.GasThresholds)
            {
                set.Gases[gas] = threshold;
            }
        }

        if (monitor.GasThresholdPrototypes != null)
        {
            foreach (var (gas, id) in monitor.GasThresholdPrototypes)
            {
                if (!set.Gases.ContainsKey(gas) && FromPrototype(id) is { } threshold)
                    set.Gases[gas] = threshold;
            }
        }

        return set.Clone();
    }

    private AtmosAlarmThreshold? FromPrototype(string? id)
    {
        return id != null && _prototypes.TryIndex<AtmosAlarmThresholdPrototype>(id, out var proto)
            ? new AtmosAlarmThreshold(proto)
            : null;
    }

    private void SendThresholds(ICommonSession session, MappingToolsThresholdsRequestEvent ev)
    {
        if (PickThresholdTarget(ev) is not { } target || Transform(target).MapUid is not { } map)
        {
            RefuseLink(session, "wf-mapping-link-not-monitor");
            return;
        }

        if (!CanEdit(new WFMappingCanEditEvent(session, map, WFMappingEditKind.Read)))
            return;

        var monitors = MonitorsOf(target).ToList();
        if (monitors.Count == 0)
        {
            RefuseLink(session, "wf-mapping-link-thresholds-no-devices");
            return;
        }

        var thresholds = EffectiveThresholds(Comp<AtmosMonitorComponent>(monitors[0]));
        RaiseNetworkEvent(new MappingToolsThresholdsEvent(GetNetEntity(target), monitors.Count, thresholds), session);
    }

    /// <summary>
    /// Sets the thresholds of every monitor the targets stand for, as one undoable edit. Values are stored on the
    /// monitor, so they save with the map and map init keeps them over the prototypes'.
    /// </summary>
    public void SetThresholds(ICommonSession session, MappingToolsSetThresholdsEvent ev)
    {
        if (ev.Targets.Count == 0 || ev.Targets.Count > MaxAccessTargets || !ValidThresholds(ev.Thresholds))
        {
            RefuseLink(session, "wf-mapping-link-bad");
            return;
        }

        var monitors = new HashSet<EntityUid>();
        var maps = new List<EntityUid>();
        foreach (var net in ev.Targets.Distinct())
        {
            if (!TryGetEntity(net, out var target) || TerminatingOrDeleted(target) ||
                Transform(target.Value).MapUid is not { } map)
            {
                continue;
            }

            foreach (var monitor in MonitorsOf(target.Value))
            {
                if (monitors.Add(monitor) && Transform(monitor).MapUid is { } monitorMap)
                    maps.Add(monitorMap);
            }

            maps.Add(map);
        }

        if (monitors.Count == 0)
        {
            RefuseLink(session, "wf-mapping-link-thresholds-no-devices");
            return;
        }

        var edit = new MappingEdit("wf-mapping-tools-edit-thresholds");
        foreach (var uid in monitors)
        {
            var monitor = Comp<AtmosMonitorComponent>(uid);
            var old = MappingThresholdState.Of(monitor);
            var updated = Merged(monitor, ev.Thresholds);
            if (!old.SameAs(updated))
                edit.Thresholds.Add(new MappingThresholdChange(GetRef(uid), old, updated));
        }

        if (edit.IsEmpty)
        {
            RefuseLink(session, "wf-mapping-link-unchanged");
            return;
        }

        if (!CanEditMaps(session, maps, edit.Thresholds.Count))
            return;

        Commit(session, edit);
        _popup.PopupCursor(Loc.GetString("wf-mapping-link-thresholds-set", ("count", edit.Thresholds.Count)), session);
        _adminLog.Add(LogType.AtmosDeviceSetting, LogImpact.Low,
            $"{session:player} used mapping tools to set the alarm thresholds of {edit.Thresholds.Count} atmos monitors");
    }

    /// <summary>A monitor's stored thresholds with the requested ones over the kinds it watches.</summary>
    private MappingThresholdState Merged(AtmosMonitorComponent monitor, MappingThresholdSet requested)
    {
        var current = EffectiveThresholds(monitor);
        var state = MappingThresholdState.Of(monitor);

        if (current.Temperature != null && requested.Temperature != null)
            state.Temperature = new AtmosAlarmThreshold(requested.Temperature);

        if (current.Pressure != null && requested.Pressure != null)
            state.Pressure = new AtmosAlarmThreshold(requested.Pressure);

        foreach (var gas in current.Gases.Keys)
        {
            if (!requested.Gases.TryGetValue(gas, out var threshold))
                continue;

            state.Gases ??= new Dictionary<Gas, AtmosAlarmThreshold>();
            state.Gases[gas] = new AtmosAlarmThreshold(threshold);
        }

        return state;
    }

    private static bool ValidThresholds(MappingThresholdSet set)
    {
        if (set.Gases.Count > Atmospherics.TotalNumberOfGases)
            return false;

        return ValidThreshold(set.Temperature) && ValidThreshold(set.Pressure) &&
               set.Gases.All(p => Enum.IsDefined(p.Key) && ValidThreshold(p.Value));
    }

    private static bool ValidThreshold(AtmosAlarmThreshold? threshold)
    {
        if (threshold == null)
            return true;

        return ValidSetting(threshold.UpperBound) && ValidSetting(threshold.LowerBound) &&
               ValidSetting(threshold.UpperWarningPercentage) && ValidSetting(threshold.LowerWarningPercentage);
    }

    private static bool ValidSetting(AlarmThresholdSetting setting)
    {
        return float.IsFinite(setting.Value) && setting.Value >= 0f;
    }

    /// <summary>Puts a monitor's stored thresholds to one side of a change.</summary>
    private void ApplyThresholds(EntityUid uid, MappingThresholdState state)
    {
        if (TerminatingOrDeleted(uid) || !TryComp(uid, out AtmosMonitorComponent? monitor))
            return;

        state.CopyTo(monitor);
    }
}
