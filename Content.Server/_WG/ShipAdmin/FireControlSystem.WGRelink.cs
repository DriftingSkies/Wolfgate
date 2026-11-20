using Content.Shared._Mono.FireControl;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem
{
    /// <summary>
    /// Puts a ship's gunnery back together at once: what each gunnery console's Refresh button does, or for a ship
    /// with no console, its servers reconnect and its guns re-register. Upstream a rebuilt gun waited up to a
    /// minute for the hardpoint refresh, and a rebuilt server left the consoles with none.
    /// </summary>
    /// <returns>Whether the ship has a gunnery server now, so a caller knows to try again once one powers up.</returns>
    public bool WgRelinkGrid(EntityUid grid)
    {
        var refreshed = false;
        var consoles = EntityQueryEnumerator<FireControlConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out var console, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            DoRefreshServer(uid, console);
            refreshed = true;
        }

        if (!refreshed)
        {
            CleanupInvalidServerReferences();
            ForceServerReconnectionOnGrid(grid);
            if (TryComp<FireControlGridComponent>(grid, out var gunnery))
                RefreshControllables(grid, gunnery);
        }

        return CompOrNull<FireControlGridComponent>(grid)?.ControllingServer is { } server && Exists(server);
    }
}
