using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for the Performance module's per-tick budgets.
/// </summary>
[CVarDefs]
public sealed class PerformanceCVars
{
    /// <summary>
    /// Milliseconds a tick may spend deleting the contents of grids queued through <c>WFGridDeleteSystem</c>. A grid
    /// and everything on it used to go in one tick (0.7 s for a big ship); now the grid leaves its map at once and its
    /// entities go over the ticks after, this much per tick. 0 deletes everything at once, as before.
    /// </summary>
    public static readonly CVarDef<float> GridDeleteBudgetMs =
        CVarDef.Create("wf.grid_delete_budget_ms", 2f, CVar.SERVERONLY);
}
