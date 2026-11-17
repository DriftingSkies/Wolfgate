using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared._WG.TestDummies;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.TestDummies;

/// <summary>Opens the test dummy window, or lists the answers to the current call in the console.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WGTestDummyCommand : LocalizedEntityCommands
{
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private WGTestDummySystem _dummies = default!;

    public override string Command => WGTestDummyCommands.Open;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 1 && args[0] == "list")
        {
            var state = _dummies.Snapshot();
            if (state.Entries.Count == 0)
            {
                shell.WriteLine(Loc.GetString("wg-test-dummy-list-empty"));
                return;
            }

            foreach (var entry in state.Entries)
            {
                shell.WriteLine(Loc.GetString("wg-test-dummy-list-line",
                    ("answer", Loc.GetString(WGTestDummyFormat.AnswerKey(entry.Answer))),
                    ("entry", WGTestDummyFormat.Describe(entry))));
            }

            return;
        }

        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        _eui.OpenEui(new WGTestDummyEui(), player);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(new[] { "list" }, Loc.GetString("cmd-testdummies-hint"))
            : CompletionResult.Empty;
    }
}
