using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared._WF.TestDummies;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.TestDummies;

/// <summary>Opens the test dummy window, or lists the answers to the current call in the console.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFTestDummyCommand : LocalizedEntityCommands
{
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private WFTestDummySystem _dummies = default!;

    public override string Command => WFTestDummyCommands.Open;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 1 && args[0] == "list")
        {
            var state = _dummies.Snapshot();
            if (state.Entries.Count == 0)
            {
                shell.WriteLine(Loc.GetString("wf-test-dummy-list-empty"));
                return;
            }

            foreach (var entry in state.Entries)
            {
                shell.WriteLine(Loc.GetString("wf-test-dummy-list-line",
                    ("answer", Loc.GetString(WFTestDummyFormat.AnswerKey(entry.Answer))),
                    ("entry", WFTestDummyFormat.Describe(entry))));
            }

            return;
        }

        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        _eui.OpenEui(new WFTestDummyEui(), player);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(new[] { "list" }, Loc.GetString("cmd-testdummies-hint"))
            : CompletionResult.Empty;
    }
}
