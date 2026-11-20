using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.Paintings;

/// <summary>Opens the admin painting browser.</summary>
[AdminCommand(AdminFlags.Moderator)]
public sealed partial class WGPaintingBrowserCommand : LocalizedCommands
{
    [Dependency] private EuiManager _eui = default!;

    public override string Command => "paintingbrowser";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        _eui.OpenEui(new WGPaintingAdminEui(), player);
    }
}
