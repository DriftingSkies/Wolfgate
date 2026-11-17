using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared._WG.Warheads;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.Warheads;

/// <summary>Opens the nuke designer.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class WGNukeDesignerCommand : LocalizedEntityCommands
{
    [Dependency] private EuiManager _eui = default!;

    public override string Command => WGNukeDesignerCommands.Open;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        _eui.OpenEui(new WGNukeDesignerEui(), player);
    }
}
