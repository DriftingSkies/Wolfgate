using Robust.Shared.Console;

namespace Content.Client._WF.Cockpit;

/// <summary>Prints why the last right-press did or did not start a cockpit pan.</summary>
public sealed class WFCockpitPanCommand : LocalizedEntityCommands
{
    [Dependency] private WFCockpitPanDiagnosticSystem _diagnostics = default!;

    public override string Command => "wf_cockpitpan";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        foreach (var line in _diagnostics.Report())
            shell.WriteLine(line);
    }
}
