using Content.Server.Administration;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.ShipAdmin;

/// <summary>Opens Shipmaster, on a ship when one is named.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WGShipAdminCommand : LocalizedEntityCommands
{
    [Dependency] private WGShipAdminSystem _ships = default!;

    public override string Command => WGShipAdminCommands.Panel;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length > 0 && args[0] == WGShipAdminCommands.SpawnArg)
        {
            _ships.Open(player, null, spawnPage: true);
            return;
        }

        EntityUid? grid = null;
        if (args.Length > 0)
        {
            grid = WGShipRefitCommand.ResolveGrid(EntityManager, shell, args[0]);
            if (grid == null)
            {
                shell.WriteError(Loc.GetString("cmd-shipmaster-not-grid", ("grid", args[0])));
                return;
            }
        }

        _ships.Open(player, grid);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(new[] { WGShipRefitCommand.Here, WGShipAdminCommands.SpawnArg }, Loc.GetString("cmd-shipmaster-hint-grid"))
            : CompletionResult.Empty;
    }
}
