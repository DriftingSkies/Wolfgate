using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.AdminObserver.Commands;

/// <summary>Opens the observer panel for the admin who runs it.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class ObserverPanelCommand : LocalizedCommands
{
    [Dependency] private IEntityManager _entities = default!;

    public override string Command => "observerpanel";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(LocalizationManager.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        _entities.System<WGObserverPanelSystem>().Open(player);
    }
}
