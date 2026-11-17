using Content.Server._WG.Shuttles;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;

namespace Content.Server._Mono.Detection;

public sealed partial class ApplyIFFFlagsToDockedShipsSystem
{
    /// <summary>
    /// Adds the host's flags to a ship, or on undock takes back the added flags that no host the ship is still
    /// docked to provides. Flags the ship had before docking stay.
    /// </summary>
    private void ApplyTrackedFlags(EntityUid gridUid, ApplyIFFFlagsToDockedShipsComponent host, bool applying)
    {
        if (applying)
        {
            var before = CompOrNull<IFFComponent>(gridUid)?.Flags ?? IFFFlags.None;
            _shuttle.AddIFFFlag(gridUid, host.Flags);
            var gained = (CompOrNull<IFFComponent>(gridUid)?.Flags ?? IFFFlags.None) & ~before;
            if (gained != IFFFlags.None)
                EnsureComp<DockedHostIffFlagsComponent>(gridUid).Added |= gained;
            return;
        }

        if (!TryComp<DockedHostIffFlagsComponent>(gridUid, out var tracked))
            return;

        var removed = tracked.Added & ~FlagsFromDockedHosts(gridUid);
        if (removed == IFFFlags.None)
            return;

        _shuttle.RemoveIFFFlag(gridUid, removed);
        tracked.Added &= ~removed;
        if (tracked.Added == IFFFlags.None)
            RemComp<DockedHostIffFlagsComponent>(gridUid);
    }

    /// <summary>
    /// The flags of every host the grid is still docked to, through any port.
    /// </summary>
    private IFFFlags FlagsFromDockedHosts(EntityUid gridUid)
    {
        // Not DockingSystem.GetDocks: it refills the set UndockDocks may be iterating while this runs.
        var flags = IFFFlags.None;
        var query = EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (query.MoveNext(out var dock, out var xform))
        {
            if (xform.GridUid != gridUid ||
                dock.DockedWith is not { } other ||
                !TryComp(other, out TransformComponent? otherXform) ||
                !TryComp<ApplyIFFFlagsToDockedShipsComponent>(otherXform.GridUid, out var host))
            {
                continue;
            }

            flags |= host.Flags;
        }

        return flags;
    }
}
