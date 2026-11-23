using Robust.Shared.Configuration;

namespace Content.Shared._WG.JoinQueue;

/// <summary>Settings for the queue players wait in when the server is at its soft player cap.</summary>
[CVarDefs]
public sealed class JoinQueueCVars
{
    /// <summary>Whether a full server queues players. Off, it refuses them as before.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.join_queue.enabled", true, CVar.SERVERONLY);

    /// <summary>Most players the queue holds; past it the server refuses the connection.</summary>
    public static readonly CVarDef<int> MaxSize =
        CVarDef.Create("wg.join_queue.max_size", 50, CVar.SERVERONLY);

    /// <summary>Seconds a queued player who disconnects keeps their place.</summary>
    public static readonly CVarDef<float> HoldSeconds =
        CVarDef.Create("wg.join_queue.hold_seconds", 120f, CVar.SERVERONLY);

    /// <summary>
    /// Whether the player count the hub and launcher show includes waiting players, so a full server with a queue
    /// reads over its cap (120/100). Off, it shows only players in the game.
    /// </summary>
    public static readonly CVarDef<bool> CountInStatus =
        CVarDef.Create("wg.join_queue.count_in_status", true, CVar.SERVERONLY);
}
