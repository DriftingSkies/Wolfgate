using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Roles;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// A commanded ship's player jobs from the admin console (the ship command console's Jobs tab does the same): list
/// them, open or close their slots, or release a player from one. Admins only, and only while the owner lets admins
/// configure the ship.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed partial class WGShipJobsCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WGShipCommandSystem _command = default!;
    [Dependency] private WGShipJobsSystem _shipJobs = default!;

    public override string Command => "wg_shipjobs";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!WGShipCommandConsole.TryGetGrid<WGShipJobsComponent>(shell, EntityManager, args[1], out var grid, out var comp)
            || !WGShipCommandConsole.CanConfigure(shell, _command, grid))
            return;

        var ship = (grid, comp);
        switch (args[0])
        {
            case "list" when args.Length == 2:
                var slots = _shipJobs.GetSlots(ship);
                var holders = _shipJobs.Holders(grid);
                foreach (var job in comp.Jobs.Keys.OrderBy(j => j.Id))
                {
                    var names = holders.Where(h => h.Comp.Job == job).Select(h => Name(h.Owner)).ToList();
                    shell.WriteLine(Loc.GetString("cmd-wg_shipjobs-line",
                        ("job", job.Id),
                        ("open", slots.GetValueOrDefault(job)),
                        ("held", names.Count == 0 ? "-" : string.Join(", ", names))));
                }
                break;
            case "open" when args.Length == 4 && int.TryParse(args[3], out var count) && count >= 0:
                if (!TrySetSlots(shell, ship, args[2], Math.Min(count, WGShipConsoleSystem.MaxJobSlots)))
                    return;
                break;
            case "close" when args.Length == 3:
                if (args[2] == "all")
                {
                    foreach (var job in comp.Jobs.Keys)
                        _shipJobs.SetSlots(ship, job, 0, shell.Player?.AttachedEntity);
                    shell.WriteLine(Loc.GetString("cmd-wg_shipjobs-closed-all"));
                }
                else if (!TrySetSlots(shell, ship, args[2], 0))
                {
                    return;
                }
                break;
            case "release" when args.Length == 3:
                var found = _shipJobs.Holders(grid).Where(h => Name(h.Owner) == args[2] || h.Comp.User.ToString() == args[2]).ToList();
                if (found.Count == 0)
                {
                    shell.WriteError(Loc.GetString("cmd-wg_shipjobs-no-holder", ("who", args[2])));
                    return;
                }

                _shipJobs.Leave(found[0]);
                shell.WriteLine(Loc.GetString("cmd-wg_shipjobs-released", ("who", args[2])));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    private string Name(EntityUid uid)
    {
        return EntityManager.GetComponent<MetaDataComponent>(uid).EntityName;
    }

    private bool TrySetSlots(IConsoleShell shell, Entity<WGShipJobsComponent> ship, string job, int count)
    {
        if (!_prototypes.HasIndex<JobPrototype>(job) || !_shipJobs.SetSlots(ship, job, count, shell.Player?.AttachedEntity))
        {
            shell.WriteError(Loc.GetString("cmd-wg_shipjobs-no-job", ("job", job)));
            return false;
        }

        shell.WriteLine(Loc.GetString("cmd-wg_shipjobs-set", ("job", job), ("count", count)));
        return true;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(new[] { "list", "open", "close", "release" }, Loc.GetString("cmd-wg_shipjobs-hint-sub")),
            2 => CompletionResult.FromHintOptions(new[] { "here" }, Loc.GetString("cmd-wg_ship-hint-grid")),
            3 when args[0] is "open" or "close" => CompletionResult.FromHint(Loc.GetString("cmd-wg_shipjobs-hint-job")),
            3 when args[0] == "release" => CompletionResult.FromHint(Loc.GetString("cmd-wg_shipjobs-hint-who")),
            4 when args[0] == "open" => CompletionResult.FromHint(Loc.GetString("cmd-wg_shipjobs-hint-count")),
            _ => CompletionResult.Empty,
        };
    }
}
