using System.Linq;
using Content.Server._WG.Administration.Systems;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._WG.Administration;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.Administration.Commands;

/// <summary>
/// SS13's "Make all areas powered/unpowered" for one grid or every grid.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class GridPowerCommand : LocalizedEntityCommands
{
    [Dependency] private GridPowerSystem _gridPower = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;

    private const string AllGrids = "all";

    public override string Command => WolfgateAdminCommands.GridPower;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 2 or > 3
            || args[0] is not ("on" or "off")
            || args.Length == 3 && !bool.TryParse(args[2], out _))
        {
            shell.WriteError(Loc.GetString("cmd-gridpower-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        var powered = args[0] == "on";
        var announce = args.Length < 3 || bool.Parse(args[2]);

        EntityUid? target = null;
        if (args[1] != AllGrids)
        {
            if (!int.TryParse(args[1], out var id)
                || !EntityManager.TryGetEntity(new NetEntity(id), out var grid)
                || !EntityManager.HasComponent<MapGridComponent>(grid))
            {
                shell.WriteError(Loc.GetString("cmd-gridpower-invalid-grid", ("grid", args[1])));
                return;
            }

            target = grid;
        }

        var count = _gridPower.SetPower(powered, target, announce);
        var state = Loc.GetString(powered ? "cmd-gridpower-powered" : "cmd-gridpower-unpowered");
        shell.WriteLine(Loc.GetString("cmd-gridpower-done", ("state", state), ("count", count)));

        var what = target is { } t ? EntityManager.ToPrettyString(t).ToString() : "every grid";
        _adminLogger.Add(LogType.AdminCommands, LogImpact.High,
            $"{shell.Player?.Name ?? "Server"} made {what} {(powered ? "powered" : "unpowered")} ({count} grids, announce {announce})");

        if (shell.Player != null)
            _gridPower.SendList(shell.Player);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHintOptions(new[] { "on", "off" }, Loc.GetString("cmd-gridpower-hint-state"));
            case 2:
                var grids = _gridPower.BuildList()
                    .OrderBy(info => info.Name)
                    .Select(info => new CompletionOption(info.Grid.ToString(), info.Name))
                    .Prepend(new CompletionOption(AllGrids));
                return CompletionResult.FromHintOptions(grids, Loc.GetString("cmd-gridpower-hint-target"));
            case 3:
                return CompletionResult.FromHintOptions(new[] { "true", "false" }, Loc.GetString("cmd-gridpower-hint-announce"));
            default:
                return CompletionResult.Empty;
        }
    }
}
