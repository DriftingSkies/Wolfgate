using Content.Server.Administration;
using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.EventStaff;

/// <summary>
/// Sends a message to the event staff chat.
/// </summary>
[AdminCommand(AdminFlags.Event)]
public sealed class EventChatCommand : LocalizedCommands
{
    [Dependency] private IChatManager _chat = default!;

    public override string Command => "esay";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        var message = string.Join(" ", args).Trim();
        if (string.IsNullOrEmpty(message))
            return;

        _chat.TrySendOOCMessage(player, message, OOCChatType.Event);
    }
}
