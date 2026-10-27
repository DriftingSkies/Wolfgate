namespace Content.Shared.Symphony;

/// <summary>
/// The contract between this build and the SSymphony panel. Bump <see cref="ModuleVersion"/> whenever the status
/// field, the refusal keys, the use of the link ticket table, the feedback table or the /symphony endpoints change;
/// the panel then says which half is behind.
/// </summary>
public static class SharedSymphony
{
    /// <summary>
    /// Reported as symphony_module in /status. The panel compares it with the version it was written against.
    /// Version 2 added /symphony/roles, the catalogue of whitelisted jobs, ghost roles and companies, and
    /// /symphony/roles/refresh, which makes the game re-read a player's rows for them. Version 3 added
    /// symphony_test_merges in /status: what a test merge build carries, from the stamp the workflow wrote. Version 4 added
    /// /symphony/hub, the hub switch: whether the server advertises itself, read and set while it runs. Version 5 added
    /// /symphony/players: who is connected, with character, job, state and ping, which /admin/info does not carry. Version 6 added
    /// symphony_round_duration and symphony_paused in /status: the round clock as the game keeps it, which stops while paused.
    /// Version 7 added /symphony/cvars: every registered CVar with its value, default and flags, and setting one while the
    /// server runs. Version 8 added player feedback: bug reports and suggestions in wolfgate_feedback_report, a table
    /// the panel reads, /symphony/feedback/status, which sets a report's status and tells its player in game, and
    /// /symphony/feedback/refresh, for after the panel has written a status itself. Version 9 added /symphony/lobby: the
    /// lobby's title screen art and music, picked from what the build ships or uploaded from the panel, shown at once,
    /// a notice box in the lobby, and /symphony/announce, a server announcement in everyone's chat. Version 10 added
    /// /symphony/commands: every console, Toolshed and client command with its description, help and the admin flags it
    /// needs, for the panel's staff handbook.
    /// </summary>
    public const int ModuleVersion = 10;

    /// <summary>
    /// Key of the one-time Discord link URL in a whitelist refusal's structured properties.
    /// </summary>
    public const string LinkKey = "symphony_link";
}
