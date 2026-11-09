using Content.Shared.Pinpointer;

namespace Content.Server.Pinpointer;

public sealed partial class NavMapSystem
{
    /// <summary>
    /// Rebuilds an outpost's nav map from scratch: a loaded outpost may bring an empty one with it, which
    /// <see cref="EnsureGridNavMap"/> would leave as it is.
    /// </summary>
    public void RefreshOutpostNavMap(EntityUid gridUid)
    {
        if (_gridQuery.TryComp(gridUid, out var mapGrid))
            RefreshGrid(gridUid, EnsureComp<NavMapComponent>(gridUid), mapGrid);
    }
}
