using Robust.Shared.Configuration;

namespace Content.Shared._WG.DeviceNetGuard;

/// <summary>Limits on how much device network traffic one tick may process.</summary>
[CVarDefs]
public sealed class DeviceNetGuardCVars
{
    /// <summary>Off restores upstream behaviour: every queued packet is sent in the tick it is due.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.device_net_guard.enabled", true, CVar.SERVERONLY);

    /// <summary>Milliseconds of packet sending per tick, after which the rest waits for the next tick.</summary>
    public static readonly CVarDef<float> TickBudgetMs =
        CVarDef.Create("wg.device_net_guard.tick_budget_ms", 8f, CVar.SERVERONLY);

    /// <summary>Packets always sent in a tick before the time budget applies, so normal traffic is never delayed.</summary>
    public static readonly CVarDef<int> MinPacketsPerTick =
        CVarDef.Create("wg.device_net_guard.min_packets_per_tick", 256, CVar.SERVERONLY);

    /// <summary>Queued packets above which the whole backlog is dropped.</summary>
    public static readonly CVarDef<int> MaxBacklog =
        CVarDef.Create("wg.device_net_guard.max_backlog", 200000, CVar.SERVERONLY);
}
