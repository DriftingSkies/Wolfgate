using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared._WG.Defcon;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.Defcon;

/// <summary>Opens the DEFCON admin panel.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WGDefconPanelCommand : LocalizedCommands
{
    [Dependency] private EuiManager _eui = default!;

    public override string Command => WGDefconCommands.Panel;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        _eui.OpenEui(new WGDefconEui(), player);
    }
}
