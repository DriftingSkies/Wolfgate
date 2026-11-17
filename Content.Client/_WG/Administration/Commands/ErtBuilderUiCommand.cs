using Content.Client._WG.Administration.UI.Ert;
using Content.Shared._WG.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Client._WG.Administration.Commands;

/// <summary>
/// Opens the ERT Builder window. Sent by the server's ertbuilder command, which does the permission check.
/// </summary>
[AnyCommand]
public sealed class ErtBuilderUiCommand : LocalizedCommands
{
    public override string Command => WolfgateAdminCommands.ErtBuilderUi;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        new ErtBuilderWindow().OpenCentered();
    }
}
