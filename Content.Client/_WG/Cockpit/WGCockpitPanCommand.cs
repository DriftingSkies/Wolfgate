using Robust.Shared.Console;

namespace Content.Client._WG.Cockpit;

/// <summary>Prints why the last right-press did or did not start a cockpit pan.</summary>
public sealed partial class WGCockpitPanCommand : LocalizedEntityCommands
{
    [Dependency] private WGCockpitPanDiagnosticSystem _diagnostics = default!;

    public override string Command => "wg_cockpitpan";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        foreach (var line in _diagnostics.Report())
            shell.WriteLine(line);
    }
}
