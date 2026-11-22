namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    /// <summary>How long an appraisal shown on a shipyard console stands before the hull is priced again.</summary>
    private static readonly TimeSpan WGShownAppraisalAge = TimeSpan.FromSeconds(15);

    /// <summary>The last appraisal of each hull shown on a console, and when it was made.</summary>
    private readonly Dictionary<EntityUid, (int Value, TimeSpan At)> _wgShownAppraisals = new();

    /// <summary>
    /// The hull's price for a console to show, at most <see cref="WGShownAppraisalAge"/> old. Opening the console and
    /// changing the ID in it each priced every entity aboard; a sale still calls <c>AppraiseGrid</c> itself.
    /// </summary>
    private int WGShownAppraisal(EntityUid grid)
    {
        var now = _timing.CurTime;
        if (_wgShownAppraisals.TryGetValue(grid, out var shown) && now - shown.At < WGShownAppraisalAge && now >= shown.At)
            return shown.Value;

        if (_wgShownAppraisals.Count > 256)
            WGForgetStaleAppraisals(now);

        var value = (int) _pricing.AppraiseGrid(grid, LacksPreserveOnSaleComp);
        _wgShownAppraisals[grid] = (value, now);
        return value;
    }

    private void WGForgetStaleAppraisals(TimeSpan now)
    {
        foreach (var (grid, shown) in _wgShownAppraisals)
        {
            if (now - shown.At >= WGShownAppraisalAge || !Exists(grid))
                _wgShownAppraisals.Remove(grid);
        }
    }
}
