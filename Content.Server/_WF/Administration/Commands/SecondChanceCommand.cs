using Content.Server._WF.Administration.Systems;
using Content.Server.Administration;
using Content.Shared._WF.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.Administration.Commands;

/// <summary>
/// Asks a body's player whether they want to return to it; a yes puts them back in and rejuvenates it.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class SecondChanceCommand : LocalizedEntityCommands
{
    [Dependency] private SecondChanceSystem _secondChance = default!;

    public override string Command => WolfgateAdminCommands.SecondChance;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-secondchance-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEntity) || !EntityManager.TryGetEntity(netEntity, out var uid))
        {
            shell.WriteError(Loc.GetString("cmd-secondchance-invalid-entity", ("entity", args[0])));
            return;
        }

        if (_secondChance.TryOffer(uid.Value, shell.Player, out var message))
            shell.WriteLine(message);
        else
            shell.WriteError(message);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHint(Loc.GetString("cmd-secondchance-hint-entity"))
            : CompletionResult.Empty;
    }
}
