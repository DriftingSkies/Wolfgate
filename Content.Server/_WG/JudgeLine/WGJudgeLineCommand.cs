using System.Linq;
using Content.Server.Administration;
using Content.Shared._WG.JudgeLine;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server._WG.JudgeLine;

/// <summary>The Judge Line from the console: list calls, place a free one for yourself or a player, or end one.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WGJudgeLineCommand : LocalizedEntityCommands
{
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private WGJudgeLineSystem _line = default!;

    public override string Command => "wg_judgeline";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length > 0 ? args[0] : string.Empty)
        {
            case "list" when args.Length == 1:
                foreach (var call in _line.Calls)
                {
                    shell.WriteLine(Loc.GetString("cmd-wg_judgeline-row", ("id", call.Id),
                        ("caller", EntityManager.ToPrettyString(call.Caller)), ("kind", call.Kind), ("phase", call.Phase)));
                }

                break;
            case "call" when args.Length is 2 or 3:
                Call(shell, args);
                break;
            case "end" when args.Length == 2 && int.TryParse(args[1], out var id):
                if (_line.Calls.FirstOrDefault(call => call.Id == id) is not { } ending || !_line.Cancel(ending))
                    shell.WriteError(Loc.GetString("cmd-wg_judgeline-no-call"));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    private void Call(IConsoleShell shell, string[] args)
    {
        if (!Enum.TryParse<WGJudgeLineKind>(args[1], true, out var kind) || !Enum.IsDefined(kind))
        {
            shell.WriteError(Loc.GetString("cmd-wg_judgeline-bad-kind"));
            return;
        }

        EntityUid? caller = shell.Player?.AttachedEntity;
        if (args.Length == 3)
            caller = _players.TryGetSessionByUsername(args[2], out var session) ? session.AttachedEntity : null;

        if (caller is not { } uid)
        {
            shell.WriteError(Loc.GetString("cmd-wg_judgeline-no-caller"));
            return;
        }

        var placed = _line.Place(uid, kind);
        shell.WriteLine(Loc.GetString("cmd-wg_judgeline-placed", ("id", placed.Id)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromOptions(new[] { "list", "call", "end" }),
            2 when args[0] == "call" => CompletionResult.FromOptions(Enum.GetNames<WGJudgeLineKind>()),
            3 when args[0] == "call" => CompletionResult.FromOptions(_players.Sessions.Select(s => s.Name)),
            _ => CompletionResult.Empty,
        };
    }
}
