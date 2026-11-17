using Robust.Shared.Console;

namespace Content.Client._WG.MappingTools;

/// <summary>
/// Opens or closes the mapping tools window. Mapping admins only, via <c>clientCommandPerms.yml</c>; the server also
/// checks the Mapping flag on every request.
/// </summary>
public sealed partial class MappingToolsCommand : LocalizedEntityCommands
{
    [Dependency] private MappingToolsSystem _tools = default!;

    public override string Command => "mappingtools";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var enabled = args.Length switch
        {
            0 => !_tools.Enabled,
            1 when bool.TryParse(args[0], out var value) => value,
            _ => (bool?) null,
        };

        if (enabled == null)
        {
            shell.WriteError(Loc.GetString("cmd-mappingtools-help", ("command", Command)));
            return;
        }

        _tools.SetEnabled(enabled.Value);
    }
}
