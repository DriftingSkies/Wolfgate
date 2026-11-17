using Robust.Shared.Console;

namespace Content.Server._WG.ShipCommand;

/// <summary>Shared argument handling for the ShipCommand console commands.</summary>
internal static class WGShipCommandConsole
{
    /// <summary>The grid an argument names: an entity id, or "here" for the grid the player stands on, with the component the command needs.</summary>
    public static bool TryGetGrid<T>(IConsoleShell shell, IEntityManager entMan, string arg, out EntityUid grid, out T comp)
        where T : IComponent
    {
        grid = default;
        comp = default!;
        if (arg == "here")
        {
            if (shell.Player?.AttachedEntity is not { } body || entMan.GetComponent<TransformComponent>(body).GridUid is not { } here)
            {
                shell.WriteError(Loc.GetString("cmd-wg_ship-not-on-grid"));
                return false;
            }

            grid = here;
        }
        else if (!NetEntity.TryParse(arg, out var net) || !entMan.TryGetEntity(net, out var uid))
        {
            shell.WriteError(Loc.GetString("cmd-wg_ship-bad-grid"));
            return false;
        }
        else
        {
            grid = uid.Value;
        }

        if (!entMan.TryGetComponent(grid, out T? found))
        {
            shell.WriteError(Loc.GetString("cmd-wg_ship-not-commanded"));
            return false;
        }

        comp = found;
        return true;
    }

    /// <summary>
    /// Whether the caller may change the ship's settings: its owner, an admin unless the owner turned that off, or the
    /// server console. Player commands check this, as the command console will.
    /// </summary>
    public static bool CanConfigure(IConsoleShell shell, WGShipCommandSystem command, EntityUid grid)
    {
        if (shell.Player == null)
            return true;

        if (shell.Player.AttachedEntity is { } body && command.CanConfigure(body, grid))
            return true;

        shell.WriteError(Loc.GetString("cmd-wg_ship-not-yours"));
        return false;
    }
}
