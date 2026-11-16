using Content.Server.Administration;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.ShipAdmin;

/// <summary>Opens Shipmaster, on a ship when one is named.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFShipAdminCommand : LocalizedEntityCommands
{
    [Dependency] private WFShipAdminSystem _ships = default!;

    private const string Here = "here";

    public override string Command => WFShipAdminCommands.Panel;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        EntityUid? grid = null;
        if (args.Length > 0)
        {
            if (args[0] == Here)
                grid = player.AttachedEntity is { } body ? EntityManager.GetComponent<TransformComponent>(body).GridUid : null;
            else if (NetEntity.TryParse(args[0], out var net) && EntityManager.TryGetEntity(net, out var named))
                grid = named;

            if (grid == null || !EntityManager.HasComponent<MapGridComponent>(grid))
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
            ? CompletionResult.FromHintOptions(new[] { Here }, Loc.GetString("cmd-shipmaster-hint-grid"))
            : CompletionResult.Empty;
    }
}
