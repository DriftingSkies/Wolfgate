using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Enums;

namespace Content.Server._WG.Subtle;

/// <summary>
/// Performs an emote that only the players right next to the sender receive.
/// </summary>
[AnyCommand]
public sealed partial class SubtleCommand : LocalizedEntityCommands
{
    [Dependency] private SubtleSystem _subtle = default!;

    public override string Command => "subtle";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (player.Status != SessionStatus.InGame)
            return;

        if (player.AttachedEntity is not { } playerEntity)
        {
            shell.WriteError(Loc.GetString("shell-must-be-attached-to-entity"));
            return;
        }

        var message = string.Join(" ", args).Trim();
        if (string.IsNullOrEmpty(message))
        {
            shell.WriteLine(Help);
            return;
        }

        _subtle.TrySendSubtle(playerEntity, message, player);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return CompletionResult.FromHint(Loc.GetString("cmd-subtle-hint"));
    }
}
