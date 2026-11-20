using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.AdminObserver.Commands;

/// <summary><c>manifest [preset]</c>: shows the admin observer to players as an avatar preset, or the default one.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class ManifestCommand : LocalizedCommands
{
    [Dependency] private IEntityManager _entities = default!;

    public const string Name = "manifest";

    public override string Command => Name;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(LocalizationManager.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        if (args.Length > 1)
        {
            shell.WriteError(Help);
            return;
        }

        var preset = args.Length == 1 ? args[0] : null;
        if (_entities.System<WGManifestSystem>().TryManifest(player, preset, out var message))
            shell.WriteLine(message);
        else
            shell.WriteError(message);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1 || shell.Player is not { } player
            || _entities.System<WGAdminObserverStore>().Get(player) is not { } data)
        {
            return CompletionResult.Empty;
        }

        return CompletionResult.FromOptions(data.Presets.Select(p => p.Name));
    }
}

/// <summary><c>unmanifest</c>: hides the admin observer from players again.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class UnmanifestCommand : LocalizedCommands
{
    [Dependency] private IEntityManager _entities = default!;

    public const string Name = "unmanifest";

    public override string Command => Name;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(LocalizationManager.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        if (_entities.System<WGManifestSystem>().TryUnmanifest(player, out var message))
            shell.WriteLine(message);
        else
            shell.WriteError(message);
    }
}
