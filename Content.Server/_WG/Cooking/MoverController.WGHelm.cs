using Content.Server._WG.Cooking;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    [Dependency] private WGMealSystem _wgMeals = default!;

    /// <summary>The best Steady meal among the ship's pilots, as a thrust factor; 1 with none.</summary>
    private float WgHelmFactor(EntityUid grid)
    {
        if (!_shuttlePilots.TryGetValue(grid, out var entry))
            return 1f;

        var best = 1f;
        foreach (var (pilot, _, _, _) in entry.Item2)
            best = Math.Max(best, _wgMeals.HelmFactor(pilot));
        return best;
    }
}
