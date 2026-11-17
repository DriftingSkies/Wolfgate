using Content.Server.Administration;
using Content.Shared._WG.Administration;
using Content.Shared.Administration;
using Robust.Server.Console;
using Robust.Shared.Console;

namespace Content.Server._WG.Administration.Commands;

/// <summary>
/// Opens the ERT Builder for the calling admin. Also gates the Wolfgate tab button.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed partial class ErtBuilderCommand : LocalizedEntityCommands
{
    [Dependency] private IServerConsoleHost _console = default!;

    public override string Command => WolfgateAdminCommands.ErtBuilder;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-ertbuilder-no-player"));
            return;
        }

        _console.RemoteExecuteCommand(player, WolfgateAdminCommands.ErtBuilderUi);
    }
}
