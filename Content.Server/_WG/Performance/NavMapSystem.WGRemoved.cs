using System.Linq;
using Content.Shared.Pinpointer;

namespace Content.Server.Pinpointer;

public sealed partial class NavMapSystem
{
    /// <summary>Most removed chunks a nav map remembers before it drops the oldest half.</summary>
    public const int WGRemovedLimit = 256;

    /// <summary>Drops the oldest removals once there are too many, and notes how far the history now reaches.</summary>
    private void WGTrimRemovedChunks(NavMapComponent component)
    {
        if (component.WGRemovedChunks.Count <= WGRemovedLimit)
            return;

        var drop = component.WGRemovedChunks.Count - WGRemovedLimit / 2;
        foreach (var (origin, tick) in component.WGRemovedChunks.OrderBy(r => r.Value).Take(drop).ToList())
        {
            component.WGRemovedChunks.Remove(origin);
            if (tick > component.WGRemovedTrimmedTo)
                component.WGRemovedTrimmedTo = tick;
        }
    }
}
