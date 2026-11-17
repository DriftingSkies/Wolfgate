using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Enums;

namespace Content.Server._WG.Subtle;

/// <summary>
/// Sends LOOC that only the players right next to the sender and online admins receive.
/// </summary>
[AnyCommand]
public sealed partial class SubtleOocCommand : LocalizedEntityCommands
{
    [Dependency] private SubtleOocSystem _subtleOoc = default!;

    public override string Command => "sooc";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (player.Status != SessionStatus.InGame)
            return;

        if (player.AttachedEntity == null)
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

        _subtleOoc.TrySendSubtleOoc(player, message);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return CompletionResult.FromHint(Loc.GetString("cmd-sooc-hint"));
    }
}
