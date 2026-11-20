using System.Linq;
using Content.Server.Administration;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.AdminObserver.Commands;

/// <summary>
/// <c>adminkit give &lt;item&gt;</c> hands the admin observer a kit catalogue item; <c>adminkit putaway</c> deletes
/// the kit item it holds; alone it opens the Kit window. Only the catalogue is on offer, so the Admin flag is not
/// a general spawn right.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class AdminKitCommand : LocalizedCommands
{
    [Dependency] private IEntityManager _entities = default!;

    public const string Name = "adminkit";
    public const string GiveArg = "give";
    public const string PutAwayArg = "putaway";

    public override string Command => Name;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var kit = _entities.System<WGAdminObserverKitSystem>();
        if (shell.Player is not { } player)
        {
            shell.WriteError(LocalizationManager.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        if (!kit.TryGetObserver(player, out var observer))
        {
            shell.WriteError(LocalizationManager.GetString("wg-observer-kit-not-observer"));
            return;
        }

        switch (args.Length > 0 ? args[0] : null)
        {
            case null:
                kit.OpenWindow(player);
                break;

            case GiveArg when args.Length == 2:
                if (!_entities.System<WGAdminKitCatalogueSystem>().Contains(args[1]))
                    shell.WriteError(LocalizationManager.GetString("wg-observer-kit-not-in-catalogue", ("id", args[1])));
                else
                    kit.Give(observer, player, args[1]);
                break;

            case PutAwayArg when args.Length == 1:
                if (kit.PutAway(observer) == null && kit.HeldPrototype(observer) is { } held)
                    shell.WriteError(LocalizationManager.GetString("wg-observer-kit-not-in-catalogue", ("id", held)));
                break;

            default:
                shell.WriteError(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromOptions(new[] { GiveArg, PutAwayArg }),
            2 when args[0] == GiveArg => CompletionResult.FromOptions(_entities.System<WGAdminKitCatalogueSystem>()
                .Categories()
                .SelectMany(c => c.Entries)
                .Select(e => e.Id)),
            _ => CompletionResult.Empty,
        };
    }
}
