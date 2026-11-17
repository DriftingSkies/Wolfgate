using Content.Server._WG.DeviceNetGuard;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Robust.Shared.Timing;

namespace Content.Server.Atmos.Monitor.Systems;

// Upstream re-broadcasts every mode an alarm receives, so two linked alarms that changed mode in one tick swapped
// modes every tick forever. Each mode change carries the tick and address it was set at; an alarm applies and
// relays only a change newer than its last, so every linked alarm ends on the newest one and the relaying stops.
public sealed partial class AirAlarmSystem
{
    [Dependency] private IGameTiming _stampTiming = default!;

    private const string ModeStampTick = "wg_air_alarm_mode_tick";
    private const string ModeStampOrigin = "wg_air_alarm_mode_origin";

    /// <summary>Stamps a mode set at this alarm itself; a received mode keeps the stamp it arrived with.</summary>
    private void StampLocalMode(EntityUid uid, string origin)
    {
        if (!TryComp<DeviceNetworkComponent>(uid, out var net) || net.Address != origin)
            return;

        var stamp = EnsureComp<AirAlarmModeStampComponent>(uid);
        stamp.Tick = _stampTiming.CurTick.Value;
        stamp.Origin = origin;
    }

    private void WriteModeStamp(EntityUid uid, NetworkPayload payload)
    {
        if (!TryComp<AirAlarmModeStampComponent>(uid, out var stamp))
            return;

        payload[ModeStampTick] = stamp.Tick;
        payload[ModeStampOrigin] = stamp.Origin;
    }

    /// <summary>Records a received mode's stamp. False if it is no newer than the mode this alarm already has.</summary>
    private bool TryAcceptModeStamp(EntityUid uid, NetworkPayload payload)
    {
        if (!payload.TryGetValue(ModeStampTick, out uint tick) || !payload.TryGetValue(ModeStampOrigin, out string? origin))
            return false;

        var stamp = EnsureComp<AirAlarmModeStampComponent>(uid);
        if (tick < stamp.Tick || tick == stamp.Tick && string.CompareOrdinal(origin, stamp.Origin) <= 0)
            return false;

        stamp.Tick = tick;
        stamp.Origin = origin;
        return true;
    }
}
