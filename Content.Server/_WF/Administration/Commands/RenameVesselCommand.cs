using System.Linq;
using Content.Server._WF.Administration.Systems;
using Content.Server.Administration;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Administration;
using Content.Shared.Administration;
using Content.Server.Shuttles.Components;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Administration.Commands;

/// <summary>
/// Renames a vessel: its grid, its late-join station and any deeds for it.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class RenameVesselCommand : LocalizedEntityCommands
{
    [Dependency] private AdminVesselSpawnSystem _vesselSpawn = default!;

    public override string Command => WolfgateAdminCommands.RenameVessel;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError(Loc.GetString("cmd-renamevessel-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netGrid)
            || !EntityManager.TryGetEntity(netGrid, out var grid)
            || !EntityManager.HasComponent<MapGridComponent>(grid))
        {
            shell.WriteError(Loc.GetString("cmd-renamevessel-not-grid", ("uid", args[0])));
            return;
        }

        var name = string.Join(' ', args.Skip(1)).Trim();
        if (name.Length == 0 || name.Length > ShuttleDeedComponent.MaxNameLength)
        {
            shell.WriteError(Loc.GetString("cmd-renamevessel-bad-name", ("max", ShuttleDeedComponent.MaxNameLength)));
            return;
        }

        if (!_vesselSpawn.TryRenameVessel(grid.Value, name, shell.Player?.AttachedEntity))
        {
            shell.WriteError(Loc.GetString("cmd-renamevessel-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-renamevessel-success", ("name", EntityManager.GetComponent<MetaDataComponent>(grid.Value).EntityName)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.Components<ShuttleComponent>(args[0]),
                Loc.GetString("cmd-renamevessel-grid-hint")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-renamevessel-name-hint")),
            _ => CompletionResult.Empty,
        };
    }
}
